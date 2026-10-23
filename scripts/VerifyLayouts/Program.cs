using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Read metadata only: do not load or execute the downloaded ClientStructs assembly.
var expected = new Dictionary<string, Dictionary<string, int>> {
    ["FFXIVClientStructs.FFXIV.Client.Game.Camera"] = new() { ["Distance"]=0x124, ["ZoomMode"]=0x180 },
    ["FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager"] = new() { ["Camera"]=0 },
    ["FFXIVClientStructs.FFXIV.Client.Graphics.PostEffect.PostEffectManager"] = new() {
        ["DepthOfFieldCoCChain"]=0x4c0, ["UpdatedDepthOfFieldChain"]=0x4f8,
        ["SceneInput"]=0x4010, ["Depth"]=0x4030, ["InitializedEffects"]=0x4410,
        ["Flags"]=0x4470, ["DepthOfField"]=0x46e8 },
    ["FFXIVClientStructs.FFXIV.Client.Graphics.PostEffect.PostEffectDepthOfFieldParameters"] = new() {
        ["UseUpdatedDepthOfField"]=0, ["UseManualDepthOfField"]=1, ["UseFocusDistanceOverride"]=2,
        ["NearFocusDistance"]=4, ["FarFocusDistance"]=8, ["FarBlurDistance"]=12,
        ["NearBlurRate"]=16, ["FarBlurRate"]=20, ["EffectMaskRate"]=28,
        ["FocusDistance"]=32, ["FNumber"]=36, ["CocNormalizationDivisor"]=40,
        ["PreviousFrameWeight"]=44, ["PreviousRangeScale"]=48 },
    ["FFXIVClientStructs.FFXIV.Client.Graphics.PostEffect.PostEffectDepthOfFieldCocLut"] = new() {
        ["FocusDistance"]=0xe0, ["FNumber"]=0xe4, ["DisableCoc"]=0xf6 }
};
using var file = File.OpenRead(args[0]);
using var pe = new PEReader(file);
var md = pe.GetMetadataReader();
foreach (var h in md.TypeDefinitions) {
    var type = md.GetTypeDefinition(h);
    var name = md.GetString(type.Namespace) + "." + md.GetString(type.Name);
    if (!expected.TryGetValue(name, out var fields)) continue;
    foreach (var f in type.GetFields()) {
        var field = md.GetFieldDefinition(f);
        var fieldName = md.GetString(field.Name);
        if (!fields.TryGetValue(fieldName, out var offset)) continue;
        if (field.GetOffset() != offset) throw new Exception($"Native layout changed: {name}.{fieldName}");
        fields.Remove(fieldName);
    }
    if (fields.Count != 0) throw new Exception($"Native fields missing: {name}");
    expected.Remove(name);
}
if (expected.Count != 0) throw new Exception("Required native types missing");
Console.WriteLine("PASS: native reference layouts match the verified game build");
