// Run with: dotnet script inspect_meai.csx
#r "nuget: Microsoft.Extensions.AI, 10.3.0"
using System.Reflection;
using Microsoft.Extensions.AI;

var t1 = typeof(AIFunctionFactoryOptions);
Console.WriteLine("=== AIFunctionFactoryOptions ===");
foreach (var p in t1.GetProperties()) Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");

var t2 = typeof(AIFunction);
Console.WriteLine("=== AIFunction abstract members ===");
foreach (var m in t2.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    Console.WriteLine($"  {m.MemberType} {m.Name}");

var t3 = typeof(AIFunctionMetadata);
Console.WriteLine("=== AIFunctionMetadata ===");
foreach (var p in t3.GetProperties()) Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");

var t4 = typeof(AIFunctionParameterMetadata);
Console.WriteLine("=== AIFunctionParameterMetadata ===");
foreach (var p in t4.GetProperties()) Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");

