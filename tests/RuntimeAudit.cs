// Framework-only reflection-only metadata/IL auditor; target code is never executed.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
public static class RuntimeAudit
{
    static Assembly target;
    static readonly string[] allowed={"mscorlib","System","System.Core","System.Xml","System.Xml.Linq"};
    static Dictionary<short,OpCode> ops=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static)
        .Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>o.Value);
    static void CheckType(Type t)
    {
        if(t==null)return;
        if(t.Assembly!=target&&!allowed.Contains(t.Assembly.GetName().Name))throw new Exception("external type");
        string n=t.FullName??t.Name;
        if(n.Contains("Window")||n.Contains("NTTab")||n.StartsWith("System.Net.")||n.StartsWith("System.Diagnostics.Process"))throw new Exception("UI/network/process type");
        if(t.HasElementType)CheckType(t.GetElementType());
        if(t.IsGenericType)foreach(var a in t.GetGenericArguments())CheckType(a);
    }
    static void CheckMember(MemberInfo m)
    {
        if(m is Type)CheckType((Type)m);
        CheckType(m.DeclaringType);
        if(m.DeclaringType!=null&&m.DeclaringType.FullName=="NinjaTrader.Cbi.Account"&&m.DeclaringType.Assembly!=target)throw new Exception("external Account API");
        if(m.DeclaringType!=null&&m.DeclaringType.FullName=="System.Reflection.Assembly"&&m.Name!="op_Inequality"&&m.Name!="op_Equality")throw new Exception("dynamic assembly access");
        if(m.Name=="LoadLibrary"||m.Name=="GetProcAddress")throw new Exception("native loading");
    }
    static void Audit(MethodBase m)
    {
        if((m.Attributes&MethodAttributes.PinvokeImpl)!=0||(m.GetMethodImplementationFlags()&(MethodImplAttributes.Native|MethodImplAttributes.Unmanaged|MethodImplAttributes.InternalCall))!=0)throw new Exception("native method");
        foreach(var p in m.GetParameters())CheckType(p.ParameterType);
        var mi=m as MethodInfo;if(mi!=null)CheckType(mi.ReturnType);
        var body=m.GetMethodBody();if(body==null)return;
        foreach(var v in body.LocalVariables)CheckType(v.LocalType);
        foreach(var e in body.ExceptionHandlingClauses)if(e.Flags==ExceptionHandlingClauseOptions.Clause)CheckType(e.CatchType);
        var il=body.GetILAsByteArray();
        for(int p=0;p<il.Length;) {
            short id=il[p++];if(id==254)id=(short)(0xfe00|il[p++]);
            OpCode op=ops[id];int n;
            switch(op.OperandType) {
                case OperandType.InlineNone:n=0;break;
                case OperandType.ShortInlineBrTarget:case OperandType.ShortInlineI:case OperandType.ShortInlineVar:n=1;break;
                case OperandType.InlineVar:n=2;break;
                case OperandType.InlineR:case OperandType.InlineI8:n=8;break;
                case OperandType.InlineSwitch:n=4+4*BitConverter.ToInt32(il,p);break;
                case OperandType.InlineSig:throw new Exception("indirect signature");
                case OperandType.InlineMethod:case OperandType.InlineType:case OperandType.InlineField:case OperandType.InlineTok:
                    CheckMember(m.Module.ResolveMember(BitConverter.ToInt32(il,p),m.DeclaringType.IsGenericType?m.DeclaringType.GetGenericArguments():null,m.IsGenericMethod?m.GetGenericArguments():null));n=4;break;
                default:n=4;break;
            }
            if(n<0||p+n>il.Length)throw new Exception("invalid IL operand");p+=n;
        }
    }
    public static int Main(string[] args)
    {
        try {
            if(args.Length!=1)throw new Exception("one artifact required");
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve+=(s,e)=> {
                if(!allowed.Contains(new AssemblyName(e.Name).Name))throw new Exception("unauthorized resolve");
                return Assembly.ReflectionOnlyLoad(e.Name);
            };
            target=Assembly.ReflectionOnlyLoadFrom(Path.GetFullPath(args[0]));
            foreach(var r in target.GetReferencedAssemblies())if(!allowed.Contains(r.Name))throw new Exception("unauthorized reference");
            int methods=0;
            foreach(var t in target.GetTypes()) {
                for(var b=t.BaseType;b!=null;b=b.BaseType)CheckType(b);
                foreach(var i in t.GetInterfaces())CheckType(i);
                foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly))CheckType(f.FieldType);
                foreach(var m in t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly).Cast<MethodBase>()
                    .Concat(t.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly))) {Audit(m);methods++;}
                if(t.TypeInitializer!=null)Audit(t.TypeInitializer);
            }
            Console.WriteLine("AUDIT_OK methods="+methods);return 0;
        } catch(Exception ex) {
            File.WriteAllText("audit-incident.txt",ex.ToString());
            Console.WriteLine("AUDIT_REJECT audit-incident.txt");return 2;
        }
    }
}
