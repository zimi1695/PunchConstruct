using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

var game = @"D:\game\steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64";
var baseLib = @"D:\game\steam\steamapps\workshop\content\2868840\3737335127\BaseLib";
var watcher = @"D:\game\steam\steamapps\workshop\content\2868840\3747526116";
AssemblyLoadContext.Default.Resolving += (_, name) => {
    foreach (var dir in new[] { game, baseLib, watcher }) {
        var path = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
var assemblyNames = args.Contains("--watcher") ? new[] { "sts2", "BaseLib", "Watcher" } : new[] { "sts2", "BaseLib" };
var assemblies = (args.Contains("--godot") ? assemblyNames.Append("GodotSharp") : assemblyNames).Select(Assembly.Load).ToArray();
var referenceSearch = args.Length > 1 && args[0] == "--references" ? new Regex(args[1], RegexOptions.IgnoreCase) : null;
var pattern = new Regex(referenceSearch != null ? "^MegaCrit" : args.Length > 0 ? args[0] : "^BaseLib.Abstracts.Custom", RegexOptions.IgnoreCase);
var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
var opcodes = typeof(OpCodes).GetFields().Where(f=>f.FieldType == typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>unchecked((ushort)o.Value));
foreach(var t in assemblies.SelectMany(a=>a.GetTypes()).Where(t=>pattern.IsMatch(t.FullName!))) {
    if (referenceSearch == null) Console.WriteLine($"\nTYPE {t.FullName} : {t.BaseType} {(t.IsAbstract ? "ABSTRACT" : "")} {(t.IsSealed ? "SEALED" : "")}");
    if (args.Contains("--types")) continue;
    if (args.Contains("--signatures")) {
        foreach(var method in t.GetMethods(flags).Cast<MethodBase>().Concat(t.GetConstructors(flags)).Where(m=> !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && !m.Name.Contains("Godot") && !m.Name.Contains("Signal")))
            Console.WriteLine($"  {method.Name}({string.Join(", ", method.GetParameters().Select(p=>$"{p.ParameterType.Name} {p.Name}"+(p.HasDefaultValue?$"={p.DefaultValue ?? "null"}":"")))})");
        continue;
    }
    foreach(var m in t.GetMembers(flags).Where(m=>referenceSearch == null && (m is PropertyInfo or FieldInfo or ConstructorInfo || m is MethodInfo mi && !mi.IsSpecialName))) {
        Console.WriteLine("  " + m + (m is MethodInfo mt ? $" [{mt.Attributes}]" : m is PropertyInfo pi ? $" [{pi.GetMethod?.Attributes}]" : ""));
    }
    if (args.Length < 2) continue;
    foreach(var m in t.GetMethods(flags).Cast<MethodBase>().Concat(t.GetConstructors(flags)).Where(m=>referenceSearch != null || Regex.IsMatch(m.Name,args[1]))) {
        var bytes=m.GetMethodBody()?.GetILAsByteArray();
        if(bytes==null) continue;
        if (referenceSearch == null) Console.WriteLine("  IL " + m);
        int i=0;
        while(i<bytes.Length) {
            int offset=i;
            ushort code=bytes[i++];
            if(code==0xfe) code=(ushort)(0xfe00|bytes[i++]);
            var op=opcodes[code]; object? val=null;
            int Read32(){int v=BitConverter.ToInt32(bytes,i);i+=4;return v;}
            switch(op.OperandType) {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineI: val=(sbyte)bytes[i++];break;
                case OperandType.ShortInlineVar: val=bytes[i++];break;
                case OperandType.InlineVar: val=BitConverter.ToUInt16(bytes,i);i+=2;break;
                case OperandType.InlineI: val=Read32();break;
                case OperandType.InlineI8: val=BitConverter.ToInt64(bytes,i);i+=8;break;
                case OperandType.ShortInlineR: val=BitConverter.ToSingle(bytes,i);i+=4;break;
                case OperandType.InlineR: val=BitConverter.ToDouble(bytes,i);i+=8;break;
                case OperandType.ShortInlineBrTarget: var b=(sbyte)bytes[i++];val=$"IL_{i+b:X4}";break;
                case OperandType.InlineBrTarget: var d=Read32();val=$"IL_{i+d:X4}";break;
                case OperandType.InlineSwitch: int n=Read32();var ds=Enumerable.Range(0,n).Select(_=>Read32()).ToArray();val=string.Join(",",ds.Select(x=>$"IL_{i+x:X4}"));break;
                default:
                    int token=Read32();
                    try { val=op.OperandType==OperandType.InlineString ? "\""+m.Module.ResolveString(token)+"\"" : m.Module.ResolveMember(token,t.IsGenericType?t.GetGenericArguments():null,m.IsGenericMethod?m.GetGenericArguments():null); }
                    catch { val=$"token:{token:X8}"; }
                    break;
            }
            if (referenceSearch == null) Console.WriteLine($"    {offset:X4} {op.Name} {val}");
            else if (val != null && referenceSearch.IsMatch(val.ToString()!)) Console.WriteLine($"REF {t.FullName}.{m.Name} -> {val}");
        }
    }
}
