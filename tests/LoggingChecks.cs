using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using GreenScreen;
internal static class LoggingChecks
{
    private static void Main(string[] args)
    {
        if (args[0]=="--artifacts")
        {
            int count=0; int expected=args[2]=="Debug" ? 3 : 2;
            foreach (string path in Directory.GetFiles(args[1],"*GreenScreen.dll",SearchOption.AllDirectories))
            {
                using var stream=File.OpenRead(path); using var pe=new PEReader(stream); var metadata=pe.GetMetadataReader(); bool found=false;
                foreach (var handle in metadata.TypeDefinitions)
                {
                    var type=metadata.GetTypeDefinition(handle);
                    if (metadata.GetString(type.Namespace)!="GreenScreen") continue;
                    if (metadata.GetString(type.Name)=="ModInfo")
                    {
                        foreach (var infoHandle in type.GetFields())
                        {
                            var info=metadata.GetFieldDefinition(infoHandle);string fieldName=metadata.GetString(info.Name);
                            if (fieldName!="Name"&&fieldName!="Version") continue;
                            string value=System.Text.Encoding.Unicode.GetString(metadata.GetBlobBytes(metadata.GetConstant(info.GetDefaultValue()).Value));
                            if (value!=(fieldName=="Name"?"Demeo Mixed Reality":"2.0.0")) throw new Exception("Wrong product name/version in "+path);
                        }
                    }
                    if (metadata.GetString(type.Name)!="ModLogging") continue;
                    foreach (var fieldHandle in type.GetFields())
                    {
                        var field=metadata.GetFieldDefinition(fieldHandle);
                        if (metadata.GetString(field.Name)!="BuildDefault") continue;
                        var value=metadata.GetBlobReader(metadata.GetConstant(field.GetDefaultValue()).Value);
                        if (value.ReadInt32()!=expected) throw new Exception("Wrong shipped default: "+path);
                        found=true;
                    }
                }
                if (!found) throw new Exception("Missing embedded logging default: "+path);
                count++;
            }
            if (count!=6) throw new Exception("Expected all six game/loader DLLs, got "+count);
            Console.WriteLine("All six actual mod DLL logging defaults PASS: "+args[2]);return;
        }
        if (ModLogging.Level.ToString()!=args[0]) throw new Exception("Wrong embedded branch default: "+ModLogging.Level);
        if (!ModLogging.Enabled(ModLogLevel.Error)||!ModLogging.Enabled(ModLogLevel.Important)) throw new Exception("Important messages suppressed");
        if (ModLogging.Enabled(ModLogLevel.Debug)!=(args[0]=="Debug")) throw new Exception("Diagnostic threshold mismatch");
        ModLogging.Configure(ModLogLevel.Debug);
        if (!ModLogging.Enabled(ModLogLevel.Debug)) throw new Exception("Explicit diagnostic override failed");
        ModLogging.Configure(ModLogLevel.Auto);
        if (ModLogging.Level.ToString()!=args[0]) throw new Exception("Auto did not restore embedded branch default");
        Console.WriteLine("Embedded branch logging default and override PASS: "+args[0]);
    }
}
