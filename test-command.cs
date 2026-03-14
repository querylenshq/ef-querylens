using Microsoft.VisualStudio.LanguageServer.Protocol;
using System.Reflection;

var cmd = new Command();
var properties = typeof(Command).GetProperties(BindingFlags.Public | BindingFlags.Instance);

Console.WriteLine("Command class properties:");
foreach (var prop in properties)
{
    Console.WriteLine($"  - {prop.Name} ({prop.PropertyType.Name})");
}
