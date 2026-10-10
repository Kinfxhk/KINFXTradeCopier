"""Pure SPECIFICATION validator: reads candidate traces; never plans or sends trades."""
from dataclasses import dataclass, replace, asdict, fields, is_dataclass
from typing import get_type_hints, get_origin, get_args
from types import UnionType
from decimal import Decimal, InvalidOperation
import json

@dataclass(frozen=True)
class Order:
    id: str
    account: str
    instrument: str
    session: str
    generation: int
    group: str
    role: str
    side: str
    filled: int
    accepted_total: int
    ack_revision: int = 0
    live: bool = True
    accepted_stop_price: str | None = None

@dataclass(frozen=True)
class Seed:
    orders: tuple[Order,...]

@dataclass(frozen=True)
class Command:
    id: str
    operation: str
    account: str
    instrument: str
    session: str
    generation: int
    order: str
    group: str
    target_session: str
    target_generation: int
    revision: int
    base_ack_revision: int
    total: int | None
    side: str | None
    order_type: str | None
    stop_price: str | None
    admitted_ms: int
    deadline_ms: int
    frontier: int

@dataclass(frozen=True)
class Plan:
    command: Command

@dataclass(frozen=True)
class Attempt:
    command_id: str
    ingress: 'Ingress | None' = None

@dataclass(frozen=True)
class Ingress:
    capture: int
    applied: int
    filled: tuple[tuple[str,int],...]
    ack: tuple[tuple[str,int,int],...]
    owned: tuple[tuple[str,str,int],...]

@dataclass(frozen=True)
class Capture:
    order: str
    seq: int
    cumulative: int

@dataclass(frozen=True)
class Apply:
    frontier: int

@dataclass(frozen=True)
class Claim:
    owned: tuple[tuple[str,str,int],...]
    capture: int
    applied: int

@dataclass(frozen=True)
class Register:
    order: Order

@dataclass(frozen=True)
class Ack:
    command_id: str
    revision: int
    account: str
    order: str
    session: str
    generation: int
    accepted_total: int
    state: str
    accepted_stop_price: str | None = None

@dataclass(frozen=True)
class Return:
    command_id: str
    outcome: str

@dataclass(frozen=True)
class Tick:
    ms: int

@dataclass(frozen=True)
class Release:
    command_id: str

@dataclass(frozen=True)
class Off:
    account: str

@dataclass(frozen=True)
class Session:
    session: str
    generation: int

@dataclass(frozen=True)
class Position:
    account: str
    instrument: str
    quantity: int

@dataclass(frozen=True)
class Request:
    id: str
    status: str
    attempted_ms: int | None = None

@dataclass(frozen=True)
class State:
    orders: tuple[Order,...] = ()
    plans: tuple[Command,...] = ()
    session: str = 'S1'
    generation: int = 1
    frontier: int = 1
    now: int = 0
    requests: tuple[Request,...] = ()
    attempt_count: int = 0
    blocked: tuple[str,...] = ()
    captures: tuple[Capture,...] = ()
    capture_frontier: int = 1
    positions: tuple[Position,...] = ()

class Violation(Exception):
    def __init__(self, prop): self.prop = prop

def decode(x):
    d=dict(x); tag=d.pop('tag')
    if tag=='seed':
        require(set(d)=={'orders'},'SCHEMA')
        return Seed(tuple(Order(**o) for o in d['orders']))
    if tag=='plan':
        require(set(d)=={'command'},'SCHEMA')
        return Plan(Command(**d['command']))
    if tag=='attempt':
        if d.get('ingress') is not None:
            g=d['ingress'];require(set(g)=={'capture','applied','filled','ack','owned'},'SCHEMA')
            d['ingress']=Ingress(g['capture'],g['applied'],tuple(tuple(t) for t in g['filled']),tuple(tuple(t) for t in g['ack']),tuple(tuple(t) for t in g['owned']))
        return Attempt(**d)
    if tag=='capture':return Capture(**d)
    if tag=='apply':return Apply(**d)
    if tag=='claim':
        require(set(d)=={'owned','capture','applied'},'SCHEMA')
        return Claim(tuple(tuple(t) for t in d['owned']),d['capture'],d['applied'])
    if tag=='register':
        require(set(d)=={'order'},'SCHEMA')
        return Register(Order(**d['order']))
    if tag=='ack':return Ack(**d)
    if tag=='return':return Return(**d)
    if tag=='tick':return Tick(**d)
    if tag=='release':return Release(**d)
    if tag=='off':return Off(**d)
    if tag=='session':return Session(**d)
    if tag=='position':return Position(**d)
    raise Violation('INPUT_TAG')

def signed_exposure(s, account, group):
    return sum(o.filled * (1 if o.side=='Buy' else -1) for o in s.orders if (o.account,o.group)==(account,group))

def require(ok, prop):
    if not ok: raise Violation(prop)

def typed(value, hint):
    if get_origin(hint) is UnionType:return any(typed(value,t) for t in get_args(hint))
    if get_origin(hint) is tuple:
        if type(value) is not tuple:return False
        args=get_args(hint)
        if len(args)==2 and args[1] is Ellipsis:return all(typed(x,args[0]) for x in value)
        return len(value)==len(args) and all(typed(x,t) for x,t in zip(value,args))
    if is_dataclass(hint):
        return type(value) is hint and all(typed(getattr(value,f.name),get_type_hints(hint)[f.name]) for f in fields(hint))
    return type(value) is hint

def captured_orders(s):
    return tuple(replace(o,filled=max([o.filled]+[e.cumulative for e in s.captures if e.order==o.id])) for o in s.orders)

def ingress(s):
    orders=captured_orders(s)
    scopes=sorted({(o.account,o.group) for o in orders})
    captured=replace(s,orders=orders)
    return Ingress(s.capture_frontier,s.frontier,tuple((o.id,o.filled) for o in orders),tuple((o.id,o.accepted_total,o.ack_revision) for o in orders),tuple((a,g,signed_exposure(captured,a,g)) for a,g in scopes))

def advance(s, e):
    if isinstance(e,Position):
        require(any((o.account,o.instrument)==(e.account,e.instrument) for o in s.orders),'TARGET_SCOPE')
        return replace(s,positions=tuple(p for p in s.positions if (p.account,p.instrument)!=(e.account,e.instrument))+(e,))
    if isinstance(e,Session):
        require(e.session and e.generation>0,'SCHEMA')
        return replace(s,session=e.session,generation=e.generation)
    if isinstance(e,Off):
        require(any(o.account==e.account for o in s.orders),'COMMAND_AUTH')
        return replace(s,blocked=tuple(sorted(set(s.blocked)|{e.account})))
    if isinstance(e,Capture):
        require(type(e.seq) is int and e.seq>s.capture_frontier,'CAPTURE_SEQUENCE')
        o=next((o for o in s.orders if o.id==e.order),None)
        require(o is not None and type(e.cumulative) is int and 0<=e.cumulative<=100000,'CAPTURE_VALUES')
        require(o.ack_revision>=0 or e.cumulative==0 or any(r.attempted_ms is not None and any(p.id==r.id and p.order==o.id and p.operation=='Submit' for p in s.plans) for r in s.requests),'FILL_CAUSALITY')
        possible=[o.accepted_total]+[p.total for p in s.plans if p.order==o.id and p.operation in ('Submit','Change') and p.total is not None and any(r.id==p.id and r.status in ('PendingAck','Unknown') for r in s.requests)]
        require(e.cumulative<=max(possible),'FILL_CAPACITY')
        return replace(s,captures=s.captures+(e,),capture_frontier=e.seq)
    if isinstance(e,Apply):
        require(type(e.frontier) is int and s.frontier<=e.frontier<=s.capture_frontier,'FRONTIER_COHERENCE')
        require(all(any(c.seq==n for c in s.captures) for n in range(s.frontier+1,e.frontier+1)),'FRONTIER_COHERENCE')
        orders=tuple(replace(o,filled=max([o.filled]+[c.cumulative for c in s.captures if c.order==o.id and c.seq<=e.frontier])) for o in s.orders)
        return replace(s,orders=orders,frontier=e.frontier)
    if isinstance(e,Claim):
        g=ingress(s)
        require((e.capture,e.applied)==(g.capture,g.applied),'FRONTIER_COHERENCE')
        require(e.owned==g.owned,'CAUSAL_EXPOSURE')
        return s
    if isinstance(e,Seed):
        require(not s.orders and not s.plans and s.now==0,'SEED_ONCE')
        require(len({o.id for o in e.orders})==len(e.orders),'ORDER_ID')
        require(all(o.side in ('Buy','Sell') and o.role in ('ENTRY','STOP','TP') and o.session==s.session and o.generation==s.generation for o in e.orders),'ORDER_SCOPE')
        require(all(len({o.instrument for o in e.orders if (o.account,o.group)==(a,g)})==1 for a,g in {(o.account,o.group) for o in e.orders}),'ORDER_SCOPE')
        require(all(type(o.filled) is int and type(o.accepted_total) is int and 0<=o.filled<=o.accepted_total<=100000 for o in e.orders),'QUANTITY')
        return replace(s,orders=e.orders)
    if isinstance(e,Register):
        require(not any(o.id==e.order.id for o in s.orders) and e.order.accepted_total==e.order.filled==0,'REGISTER')
        require(e.order.side in ('Buy','Sell') and e.order.role in ('ENTRY','STOP','TP'),'ORDER_SCOPE')
        require(all(o.instrument==e.order.instrument for o in s.orders if (o.account,o.group)==(e.order.account,e.order.group)),'ORDER_SCOPE')
        return replace(s,orders=s.orders+(replace(e.order,ack_revision=-1,accepted_stop_price=None),))
    if isinstance(e,Plan):
        require(e.command.revision>0 and e.command.generation>0 and e.command.target_generation>0,'SCHEMA')
        require(e.command.account not in s.blocked,'COMMAND_AUTH')
        require(type(e.command.admitted_ms) is int and type(e.command.deadline_ms) is int and e.command.admitted_ms==s.now and e.command.deadline_ms==e.command.admitted_ms+100,'DEADLINE')
        require(not any(c.id==e.command.id for c in s.plans),'COMMAND_ID')
        return replace(s,plans=s.plans+(e.command,),requests=s.requests+(Request(e.command.id,'QueuedNeverSubmitted'),))
    if isinstance(e,Tick):
        require(type(e.ms) is int and e.ms>=s.now,'CLOCK')
        requests=tuple(replace(r,status='ExpiredNeverSubmitted') if r.status=='QueuedNeverSubmitted' and e.ms>=next(c.deadline_ms for c in s.plans if c.id==r.id) else replace(r,status='Unknown') if r.status=='PendingAck' and e.ms>=r.attempted_ms+500 else r for r in s.requests)
        blocked=tuple(sorted(set(s.blocked)|{c.account for c in s.plans if any(r.id==c.id and r.status=='Unknown' for r in requests)}))
        return replace(s,now=e.ms,requests=requests,blocked=blocked)
    if isinstance(e,Release):
        require(any(r.id==e.command_id for r in s.requests),'COMMAND_ID')
        return s
    if isinstance(e,Return):
        require(e.outcome in ('normal','exception'),'RETURN')
        require(any(r.id==e.command_id and r.attempted_ms is not None for r in s.requests),'RETURN')
        return s
    if isinstance(e,Ack):
        c=next((c for c in s.plans if c.id==e.command_id),None)
        require(c is not None and (e.revision,e.account,e.order,e.session,e.generation)==(c.revision,c.account,c.order,c.session,c.generation),'ACK_COMMAND_SCOPE')
        require((e.session,e.generation)==(s.session,s.generation),'ACK_COMMAND_SCOPE')
        require(any(r.id==c.id and r.status in ('PendingAck','Unknown') for r in s.requests),'ACK_COMMAND_SCOPE')
        require(e.state in (('Cancelled',) if c.operation=='Cancel' else ('Working','PartFilled')),'ACK_STATE')
        require(e.accepted_total==c.total if c.total is not None else 0<=e.accepted_total<=100000 and all(e.accepted_total==o.accepted_total for o in s.orders if o.id==e.order),'ACK_TOTAL')
        require(all(o.filled<=e.accepted_total for o in captured_orders(s) if o.id==e.order),'ACK_FILL_COHERENCE')
        if e.accepted_stop_price is not None:require(e.accepted_stop_price==c.stop_price,'ACK_PRICE')
        orders=tuple(replace(o,accepted_total=e.accepted_total,ack_revision=e.revision,live=e.state!='Cancelled',accepted_stop_price=e.accepted_stop_price if e.accepted_stop_price is not None else o.accepted_stop_price) if o.id==e.order else o for o in s.orders)
        return replace(s,orders=orders,requests=tuple(replace(r,status='Acknowledged') if r.id==c.id else r for r in s.requests))
    if isinstance(e,Attempt):
        c=next((c for c in s.plans if c.id==e.command_id),None)
        require(c is not None,'COMMAND_ID')
        require(next(r for r in s.requests if r.id==c.id).status=='QueuedNeverSubmitted','NO_RESEND')
        require(not any(r.status in ('PendingAck','Unknown') and any(p.id==r.id and p.order==c.order for p in s.plans) for r in s.requests),'NO_RESEND')
        require((c.session,c.generation)==(s.session,s.generation),'COMMAND_AUTH')
        require(c.account not in s.blocked,'COMMAND_AUTH')
        require(c.frontier==s.frontier and s.frontier==s.capture_frontier,'FRONTIER')
        if e.ingress is not None:require(e.ingress==ingress(s),'INGRESS_VALUES')
        if c.operation=='Flatten':
            require(c.order=='*' and c.total is None and c.order_type is None and c.side is None,'FLATTEN_PROOF')
            scoped=[o for o in s.orders if (o.account,o.instrument)==(c.account,c.instrument)]
            require(scoped and all((o.session,o.generation)==(c.target_session,c.target_generation) for o in scoped),'TARGET_SCOPE')
            require(not any(r.status in ('PendingAck','Unknown') and any(p.id==r.id and p.account==c.account for p in s.plans) for r in s.requests),'NO_RESEND')
            for g in {o.group for o in scoped}:
                exp=signed_exposure(s,c.account,g)
                require(not exp or any(o.group==g and o.role=='STOP' and o.live and o.ack_revision>=0 and o.side==('Sell' if exp>0 else 'Buy') and o.accepted_total-o.filled>=abs(exp) for o in scoped),'PRESERVE_STOP')
            return replace(s,requests=tuple(replace(r,status='PendingAck',attempted_ms=s.now) if r.id==c.id else r for r in s.requests),attempt_count=s.attempt_count+1,blocked=tuple(sorted(set(s.blocked)|{c.account})))
        o=next((o for o in s.orders if o.id==c.order),None)
        require(o is not None,'TARGET_SCOPE')
        require(o.live,'TARGET_SCOPE')
        require((o.account,o.instrument,o.session,o.generation,o.group)==(c.account,c.instrument,c.target_session,c.target_generation,c.group),'TARGET_SCOPE')
        if c.operation!='Submit':require(o.ack_revision>=0 and c.base_ack_revision==o.ack_revision,'ACK_BASE')
        else:require(o.ack_revision==-1,'ACK_BASE')
        require(c.revision==(1 if c.operation=='Submit' else o.ack_revision+1),'ACK_REVISION')
        exp=signed_exposure(s,c.account,c.group)
        if c.operation in ('Submit','Change'):
            require(c.side in ('Buy','Sell'),'STOP_SIDE')
            require(type(c.total) is int and 0<c.total<=100000,'QUANTITY')
            if o.role=='STOP':
                require(c.order_type=='StopMarket' and c.stop_price is not None,'STOP_TYPE')
                try: price=Decimal(c.stop_price)
                except InvalidOperation:raise Violation('STOP_TYPE')
                require(price.is_finite() and 0<price<=1000000,'STOP_TYPE')
                require(not exp or c.side==('Sell' if exp>0 else 'Buy'),'STOP_SIDE')
                require(c.total>=o.filled+abs(exp),'STOP_TOTAL')
            require(c.side==o.side,'ORDER_SIDE')
        elif c.operation=='Cancel':
            require(c.total is None and c.order_type is None,'CANCEL_FIELDS')
            if o.role=='STOP' and exp:
                revoked={p.order for p in s.plans if p.operation=='Cancel' and any(r.id==p.id and r.status in ('PendingAck','Unknown','Acknowledged') for r in s.requests)}
                def remaining(a):
                    totals=[a.accepted_total]+[p.total for p in s.plans if p.order==a.id and p.operation=='Change' and any(r.id==p.id and r.status in ('PendingAck','Unknown') for r in s.requests)]
                    if a.accepted_stop_price is None and any(p.order==a.id and p.operation=='Submit' and any(r.id==p.id and r.attempted_ms is not None for r in s.requests) for p in s.plans):return 0
                    return max(0,min(totals)-a.filled)
                alternatives=[a for a in s.orders if a.id!=o.id and a.id not in revoked and a.live and a.ack_revision>=0 and (a.account,a.instrument,a.session,a.generation,a.group)==(o.account,o.instrument,o.session,o.generation,o.group) and a.role=='STOP' and a.side==('Sell' if exp>0 else 'Buy') and remaining(a)>=abs(exp)]
                require(bool(alternatives),'PRESERVE_STOP')
        else:raise Violation('OPERATION')
        return replace(s,requests=tuple(replace(r,status='PendingAck',attempted_ms=s.now) if r.id==c.id else r for r in s.requests),attempt_count=s.attempt_count+1)
    raise Violation('INPUT_TAG')

def validate(inputs):
    s=State(); cuts=[]
    for i,x in enumerate(inputs,1):
        before=asdict(s)
        try:
            require(i<=1024 and len(json.dumps(x,ensure_ascii=False).encode('utf-8'))<=65536,'RESOURCE_BOUND')
            e=decode(x)
            if isinstance(e,Plan) and e.command.total is not None:require(type(e.command.total) is int,'QUANTITY')
            require(typed(e,type(e)),'SCHEMA'); s=advance(s,e)
            cuts.append({'event':i,'before':before,'after':asdict(s)})
        except Violation as ex:
            return {'accepted':False,'failure':{'event':i,'property':ex.prop,'before':before,'after':asdict(s)},'cuts':cuts,'state':asdict(s)}
        except (KeyError,TypeError,ValueError,StopIteration) as ex:
            return {'accepted':False,'failure':{'event':i,'property':'SCHEMA','before':before,'after':asdict(s),'error_type':type(ex).__name__},'cuts':cuts,'state':asdict(s)}
    return {'accepted':True,'failure':None,'cuts':cuts,'state':asdict(s)}
