using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using GreenScreen;

namespace GreenScreen { public static class Utils { public static void LogInfo(string message) { } } }

internal static class IdentityChecks
{
    private static int _checks;
    private static void Check(bool value,string name) { if (!value) throw new Exception(name); _checks++; }
    private static SteamLinkConnection.Snapshot Frame() => SteamLinkConnection.Classify(true,1,"cv","Deckard MP","");
    public static void Main(string[] args)
    {
        if (args.Length==1 && args[0]=="--artifacts")
        {
            foreach (string game in new[] { "DEMEO","DEMEOBATTLES","DEMEOBATTLEMARKED" })
                foreach (string loader in new[] { "BepInEx","MelonLoader" })
                {
                    string name=game=="DEMEO" ? "Demeo" : game=="DEMEOBATTLES" ? "DemeoBattles" : "DemeoBattlemarked";
                    string framework=game=="DEMEO" ? "netstandard2.1" : "net6.0";
                    string path=Path.GetFullPath(Path.Combine("bin",loader,game,framework,name+"_GreenScreen.dll"));
                    using (var executable=new PEReader(File.OpenRead(path)))
                    {
                        var metadata=executable.GetMetadataReader();
                        bool Calls(string type,string method) => metadata.MemberReferences.Any(handle=>
                        {
                            var member=metadata.GetMemberReference(handle);
                            return metadata.GetString(member.Name)==method && member.Parent.Kind==HandleKind.TypeReference
                                && metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)member.Parent).Name)==type;
                        });
                        Check(Calls("XRRenderPass","GetRenderParameter")== (game=="DEMEO"),
                            "produced IL2CPP mods contain no unsafe XR parameter wrapper call; Mono probe retained: "+loader+"/"+game);
                        Check(Calls("AsyncGPUReadback","Request")== (game=="DEMEO"),
                            "produced IL2CPP mods cannot issue optional Unity XR GPU readbacks; Mono probe retained: "+loader+"/"+game);
                        Check(!Calls("AccessTools","TypeByName"),"produced mods avoid global type enumeration of invalid interop types: "+loader+"/"+game);
                        bool HasFadeMethod(string name) => metadata.TypeDefinitions.Any(handle=>
                        {
                            var type=metadata.GetTypeDefinition(handle);
                            return metadata.GetString(type.Name)=="PassthroughFade" && type.GetMethods().Any(method=>
                                metadata.GetString(metadata.GetMethodDefinition(method).Name)==name);
                        });
                        Check(HasFadeMethod("BeforeControllerConstructor")== (game=="DEMEO"),
                            "only Mono may hook the original fade constructor; allocating IL2CPP wrappers are excluded: "+loader+"/"+game);
                        Check(HasFadeMethod("BeforeNativeGameFade")== (game=="DEMEOBATTLES"),
                            "only Battles repairs the exact cached replacement before native fade writes: "+loader+"/"+game);
                        string expected=metadata.GetAssemblyDefinition().Version.ToString(3);
                        string attributeVersion=null;
                        foreach (var handle in metadata.CustomAttributes)
                        {
                            var attribute=metadata.GetCustomAttribute(handle);
                            if (attribute.Constructor.Kind!=HandleKind.MemberReference) continue;
                            var constructor=metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                            if (constructor.Parent.Kind!=HandleKind.TypeReference) continue;
                            var type=metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
                            string attributeName=metadata.GetString(type.Name);
                            if (attributeName!=(loader=="BepInEx" ? "BepInPlugin" : "MelonInfoAttribute")) continue;
                            var blob=metadata.GetBlobReader(attribute.Value);
                            Check(blob.ReadUInt16()==1,"valid loader attribute encoding: "+loader+"/"+game);
                            blob.ReadSerializedString(); // Plugin GUID or Melon System.Type.
                            blob.ReadSerializedString(); // Display name.
                            attributeVersion=blob.ReadSerializedString();
                        }
                        Check(attributeVersion==expected,"loader display version agrees with produced assembly version "+expected+": "+loader+"/"+game);
                    }
                    var assembly=System.Reflection.Assembly.LoadFile(path);
                    foreach (bool dll in new[] { false,true })
                    {
                        string resource=dll ? SteamLinkConnection.ApiResource : SteamLinkConnection.Resource;
                        using var stream=assembly.GetManifestResourceStream(resource);
                        using var bytes=new MemoryStream();
                        Check(stream!=null,"produced mod contains identity resource: "+loader+"/"+game+"/"+resource);
                        stream.CopyTo(bytes);
                        Check(SteamLinkConnection.ArtifactPin(bytes.ToArray(),dll ? SteamLinkConnection.ApiBytes : SteamLinkConnection.HelperBytes,
                            dll ? SteamLinkConnection.ApiSha256 : SteamLinkConnection.HelperSha256,dll),
                            "produced mod resource has the correct PE identity and pin: "+loader+"/"+game);
                    }
                    using var fade=assembly.GetManifestResourceStream("GreenScreen.NativeFade");
                    Check(fade!=null,"produced mod includes its passthrough fade: "+loader+"/"+game);
                    using var fadeBytes=new MemoryStream(); fade.CopyTo(fadeBytes);
                    Check(fadeBytes.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes("assets/nativefade.bundle")),
                        "produced mod embeds the exact validated fade: "+loader+"/"+game);
                    using var coverage=assembly.GetManifestResourceStream("GreenScreen.NativeCoverage");
                    Check(coverage!=null,"produced mod contains the verified coverage bundle: "+loader+"/"+game);
                    using var bundledBytes=new MemoryStream(); coverage.CopyTo(bundledBytes);
                    Check(bundledBytes.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes("assets/nativecoverage.bundle")),
                        "produced mod embeds the exact validated bundle: "+loader+"/"+game);
                }
            Console.WriteLine("Produced mod resources and XR diagnostic boundary checks passed: "+_checks);
            return;
        }
        Check(Frame().Available,"observed Frame/Steam Link hardware profile");
        Check(SteamLinkConnection.Classify(true,1,"vrlink","Steam Frame","vrlink/frame").Available,"canonical Frame/Steam Link profile");
        foreach (string model in new[] { "Quest 3","Valve Index","Vive","Steam Frame 2","Generic HMD","" })
            Check(!SteamLinkConnection.Classify(true,1,"vrlink",model,"vrlink/frame").Available,"unrecognized headset refused: "+model);
        foreach (string driver in new[] { "VirtualDesktop","oculus","lighthouse","cv","vrlink-copy","" })
            Check(!SteamLinkConnection.Classify(true,1,driver,"Steam Frame",driver).Available,"wrong or ambiguous transport refused: "+driver);
        Check(!SteamLinkConnection.Classify(true,1,"VirtualDesktop","Deckard MP","").Available,"Frame through Virtual Desktop refused");
        Check(!SteamLinkConnection.Classify(false,1,"vrlink","Steam Frame","vrlink/frame").Available,"disconnected HMD refused");
        Check(!SteamLinkConnection.Classify(true,2,"vrlink","Steam Frame","vrlink/frame").Available,"controller identity cannot grant HMD permission");
        Check(!SteamLinkConnection.Classify(true,1,"cv","Deckard MP","VirtualDesktop/frame").Available,"observed profile with foreign registered driver refused");
        string json="{\"schema\":\"ghvr-steamlink-connection-v1\",\"connected\":true,\"device_class\":1,\"tracking_system\":\"cv\",\"model\":\"Deckard MP\",\"registered_type\":\"\"}";
        Check(SteamLinkConnection.Decode(json,0).Available,"actual helper JSON decoded with the framework serializer");
        foreach (string malformed in new[] { "", "null", json+"{}",json.Replace("\"cv\"","null"),
            json.Replace("\"connected\":true","\"connected\":false"),json.Replace("\"connected\":true","\"connected\":true,\"connected\":true"),
            json.Replace("Deckard MP","Quest 3"), json.Replace("registered_type","unknown"),json.Replace("\"cv\"","\"cv\\n\""),new string('x',2049) })
            Check(!SteamLinkConnection.Decode(malformed,0).Available,"malformed or unsupported identity fails closed: "+malformed);
        Check(!SteamLinkConnection.Decode(json,2).Available,"nonzero child exit cannot grant admission");

        int calls=0; var pending=new TaskCompletionSource<SteamLinkConnection.Snapshot>();
        var monitor=new SteamLinkConnection.Monitor(_=> { calls++; return pending.Task; });
        monitor.Tick(true,0); Check(!monitor.Current.Available && calls==1,"pending reader is not permission");
        pending.SetResult(Frame()); monitor.Tick(true,.1);
        Check(monitor.Current.Available,"completed Frame identity grants cached admission");
        pending=new TaskCompletionSource<SteamLinkConnection.Snapshot>(); monitor.Tick(true,2);
        Check(calls==2 && monitor.Current.Available,"positive identity is rechecked without toggling between ordinary polls");
        pending.SetResult(SteamLinkConnection.Classify(true,1,"vrlink","Quest 3","vrlink/frame")); monitor.Tick(true,2.1);
        Check(!monitor.Current.Available,"headset replacement revokes admission on the next identity result");
        monitor.Reset(); pending=new TaskCompletionSource<SteamLinkConnection.Snapshot>(); monitor.Tick(true,3);
        monitor.Invalidate(); Check(!monitor.Current.Available,"device invalidation immediately clears positive identity");
        pending.SetResult(Frame()); monitor.Tick(true,3.1);
        Check(!monitor.Current.Available,"stale child from before device change cannot regrant admission");
        monitor.Reset(); pending=new TaskCompletionSource<SteamLinkConnection.Snapshot>(); monitor.Tick(true,4);
        pending.SetResult(Frame()); monitor.Tick(true,4.1);
        pending=new TaskCompletionSource<SteamLinkConnection.Snapshot>(); monitor.Tick(true,6);
        monitor.Tick(true,12.2);
        Check(!monitor.Current.Available,"hung refresh cannot retain an old positive identity indefinitely");
        monitor.Reset(); pending.SetResult(Frame()); monitor.Tick(false,13);
        Check(!monitor.Current.Available,"completed reader cannot grant admission after session shutdown");

        int retries=0;
        var absent=new SteamLinkConnection.Monitor(_=> { retries++; return Task.FromResult(new SteamLinkConnection.Snapshot(SteamLinkConnection.Reason.NativeUnavailable)); });
        for (int i=0;i<20;i++) absent.Tick(true,i*2);
        Check(retries>3 && !absent.Current.Available,"late Steam Link connection remains discoverable after early failures");

        byte[] helper=SteamLinkConnection.ReadResource(SteamLinkConnection.Resource,SteamLinkConnection.HelperBytes,SteamLinkConnection.HelperSha256,false);
        byte[] api=SteamLinkConnection.ReadResource(SteamLinkConnection.ApiResource,SteamLinkConnection.ApiBytes,SteamLinkConnection.ApiSha256,true);
        Check(helper!=null && api!=null,"embedded reader and public OpenVR SDK have the expected PE architecture, length and hashes");
        helper[helper.Length-1]^=1;
        Check(!SteamLinkConnection.HelperPin(helper),"changed helper cannot execute");
        string temp=Path.Combine(Path.GetTempPath(),"demeo-identity-"+Guid.NewGuid().ToString("N"));
        try
        {
            string stage="";
            string path=SteamLinkConnection.ExtractResource(SteamLinkConnection.ApiResource,SteamLinkConnection.ApiBytes,
                SteamLinkConnection.ApiSha256,"openvr_api.dll",true,temp,_=>true,ref stage);
            Check(path!=null && File.Exists(path),"resource preparation extracts the public SDK into an owned cache");
            File.WriteAllText(path,"corrupted");
            bool refused=false;
            try { refused=SteamLinkConnection.ExtractResource(SteamLinkConnection.ApiResource,SteamLinkConnection.ApiBytes,
                SteamLinkConnection.ApiSha256,"openvr_api.dll",true,temp,_=>true,ref stage)==null; }
            catch (InvalidDataException) { refused=true; }
            Check(refused,"changed cached SDK is rejected");
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp,true); }
        Console.WriteLine("Frame/Steam Link identity checks passed: "+_checks);
    }
}
