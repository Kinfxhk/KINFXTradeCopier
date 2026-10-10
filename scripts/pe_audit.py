"""Read ECMA-335 PE metadata with stdlib only. Never load an assembly."""
import struct

# Complete table row layouts through GenericParamConstraint (ECMA-335 II.22).
# H/I: integers, s/b/g: heap indexes, tN: table index, cNAME: coded index.
SCHEMA = [
 'H s g g g', 'cResolutionScope s s', 'I s s cTypeDefOrRef t4 t6', 't4', 'H s b', 't6',
 'I H H s b t8', 't8', 'H H s', 't2 cTypeDefOrRef', 'cMemberRefParent s b',
 'H cHasConstant b', 'cHasCustomAttribute cCustomAttributeType b', 'cHasFieldMarshal b',
 'H cHasDeclSecurity b', 'H I t2', 'I t4', 'b', 't2 t20', 't20', 'H s cTypeDefOrRef',
 't2 t23', 't23', 'H s b', 'H t6 cHasSemantics', 't2 cMethodDefOrRef cMethodDefOrRef',
 's', 'b', 'H cMemberForwarded s t26', 'I t4', 'I I', 'I', 'I H H H H I b s s',
 'I', 'I I I', 'H H H H I b s s b', 'I t35', 'I I I t35', 'I s b',
 'I I s s cImplementation', 'I I s cImplementation', 't2 t2',
 'H H cTypeOrMethodDef s', 'cMethodDefOrRef b', 't42 cTypeDefOrRef'
]
CODED = {
 'ResolutionScope': (2, [0, 26, 35, 1]), 'TypeDefOrRef': (2, [2, 1, 27]),
 'MemberRefParent': (3, [2, 1, 26, 6, 27]), 'HasConstant': (2, [4, 8, 23]),
 'HasCustomAttribute': (5, [6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17, 26, 27, 32, 35, 38, 39, 40, 42, 44, 43]),
 'CustomAttributeType': (3, [6, 10]), 'HasFieldMarshal': (1, [4, 8]),
 'HasDeclSecurity': (2, [2, 6, 32]), 'HasSemantics': (1, [20, 23]),
 'MethodDefOrRef': (1, [6, 10]), 'MemberForwarded': (1, [4, 6]),
 'Implementation': (2, [38, 35, 39]), 'TypeOrMethodDef': (1, [2, 6])
}


def metadata(data):
    """Return declared references and tables; malformed metadata fails closed."""
    def unpack(fmt, at):
        size = struct.calcsize('<' + fmt)
        if at < 0 or at + size > len(data):
            raise ValueError('truncated PE metadata')
        return struct.unpack_from('<' + fmt, data, at)
    if data[:2] != b'MZ':
        raise ValueError('not a PE')
    pe = unpack('I', 60)[0]
    if data[pe:pe+4] != b'PE\0\0':
        raise ValueError('invalid PE signature')
    section_count = unpack('H', pe + 6)[0]
    opt_size = unpack('H', pe + 20)[0]
    opt = pe + 24
    magic = unpack('H', opt)[0]
    if magic not in (0x10b, 0x20b):
        raise ValueError('unsupported PE optional header')
    directories = opt + (96 if magic == 0x10b else 112)
    sections = [unpack('IIII', opt + opt_size + 40*i + 8) for i in range(section_count)]
    def offset(rva):
        for virtual_size, virtual, raw_size, raw in sections:
            if virtual <= rva < virtual + min(virtual_size, raw_size):
                return raw + rva - virtual
        raise ValueError('unmapped PE RVA')
    cli_rva, cli_size = unpack('II', directories + 14*8)
    if cli_size < 72:
        raise ValueError('missing CLI header')
    cli = offset(cli_rva)
    md_rva, md_size = unpack('II', cli + 8)
    md = offset(md_rva)
    if md + md_size > len(data) or data[md:md+4] != b'BSJB':
        raise ValueError('invalid CLI metadata root')
    version_len = unpack('I', md + 12)[0]
    p = md + 16 + ((version_len + 3)//4)*4
    count = unpack('H', p + 2)[0]
    p += 4
    streams = {}
    for _ in range(count):
        relative, size = unpack('II', p)
        p += 8
        end = data.find(b'\0', p, min(p+32, len(data)))
        if end < 0:
            raise ValueError('invalid metadata stream name')
        name = data[p:end].decode('ascii')
        p += ((end-p+1+3)//4)*4
        if relative + size > md_size:
            raise ValueError('metadata stream outside root')
        streams[name] = (md + relative, size)
    if '#~' not in streams or '#Strings' not in streams:
        raise ValueError('required metadata streams missing')
    tab, _ = streams['#~']
    heaps = unpack('B', tab + 6)[0]
    valid = unpack('Q', tab + 8)[0]
    if valid >> len(SCHEMA):
        raise ValueError('unsupported metadata table')
    counts = [0]*len(SCHEMA)
    p = tab + 24
    for i in range(len(SCHEMA)):
        if valid & (1 << i):
            counts[i] = unpack('I', p)[0]
            p += 4
    def width(kind):
        if kind == 'H': return 2
        if kind == 'I': return 4
        if kind in 'sbg': return 4 if heaps & {'s': 1, 'g': 2, 'b': 4}[kind] else 2
        if kind[0] == 't': return 4 if counts[int(kind[1:])] >= 65536 else 2
        bits, tables = CODED[kind[1:]]
        return 4 if max(counts[t] for t in tables) >= (1 << (16-bits)) else 2
    tables = []
    for i, schema in enumerate(SCHEMA):
        kinds = schema.split()
        rows = []
        for _ in range(counts[i]):
            row = []
            for kind in kinds:
                size = width(kind)
                row.append(unpack('I' if size == 4 else 'H', p)[0])
                p += size
            rows.append(row)
        tables.append(rows)
    if p > tab + streams['#~'][1]:
        raise ValueError('metadata tables exceed stream')
    strings, string_size = streams['#Strings']
    def string(index):
        if not 0 <= index < string_size: raise ValueError('invalid string index')
        end = data.find(b'\0', strings+index, strings+string_size)
        if end < 0: raise ValueError('unterminated string heap')
        return data[strings+index:end].decode('utf-8', errors='strict')
    return {'references': [string(row[6]) for row in tables[35]],
            'name': string(tables[32][0][7]) if tables[32] else None,
            'pinvoke': any(row[2] & 0x2000 for row in tables[6]),
            'types': [string(row[2]) + '.' + string(row[1]) for row in tables[2]]}
