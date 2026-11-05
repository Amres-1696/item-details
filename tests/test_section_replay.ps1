#Requires -Version 7.4
param([Parameter(Mandatory)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$managed = Join-Path $GameDir 'Probably Stolen_Data/Managed'
$refs = @('Assembly-CSharp.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.UI.dll', '0Harmony.dll') | ForEach-Object {
    $path = Join-Path $managed $_
    [void][Reflection.Assembly]::LoadFrom($path)
    $path
}
# Compile a separate consumer assembly using only game/framework references.
$source = Get-Content (Join-Path $repo 'sdk/ItemDetailsClient.cs') -Raw
$source += @'

public static class ReplayProbe
{
    public static IDisposable Watch() => ItemDetailsClient.WatchTooltipSections();
    public static IDisposable ClaimAll() =>
        ItemDetailsClient.RegisterTooltipSections((item, builder, section) => true);
    public static bool SectionAvailable() => ItemDetailsClient.SectionProtocolAvailable;
    public static bool LiquidAvailable() => ItemDetailsClient.LiquidProtocolAvailable;
    public static string Ids(RichTextBuilder builder) =>
        ItemDetailsClient.TryGetTooltipSections(builder, out var sections)
            ? string.Join(",", System.Linq.Enumerable.Select(sections, s => s.Id)) : "";
    public static string Runs(RichTextBuilder builder)
    {
        if (!ItemDetailsClient.TryGetTooltipSections(builder, out var sections)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var s in sections)
        {
            sb.Append(s.Id).Append('|');
            foreach (var f in s.Fields) sb.Append(f.Key).Append('=').Append(f.Value).Append(';');
            foreach (var r in s.Runs) sb.Append(r.Kind).Append(':').Append(r.Text).Append(':').Append(r.Color).Append(';');
        }
        return sb.ToString();
    }
    public static bool UsesStableIds(RichTextBuilder builder) =>
        Ids(builder) == TooltipSectionIds.MachineSpeed + "," + TooltipSectionIds.Appraisal;
}
'@
Add-Type -TypeDefinition $source -ReferencedAssemblies ($refs + @(Get-ChildItem "$PSHOME/ref/*.dll" | ForEach-Object FullName))
function Check([bool]$condition, [string]$name) {
    if (!$condition) { throw "FAIL $name" }
    Write-Output "PASS $name"
}
$probe = [ItemDetails.Integration.ReplayProbe]
$provider = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'bin/Release/netstandard2.1/ItemDetails.dll'))
$flags = [Reflection.BindingFlags]'NonPublic,Static'
$sectionApi = $provider.GetType('ItemDetails.TooltipSections')
$tp = $provider.GetType('ItemDetails.TooltipPipeline')
$writerType = $provider.GetType('ItemDetails.TooltipSections+Writer')
$rw = [Reflection.BindingFlags]'NonPublic,Instance'
$entryType = $provider.GetType('ItemDetails.ModEntry')
$entryObject = [Activator]::CreateInstance($entryType)
$breakdown = $provider.GetType('ItemDetails.ValueBreakdown')

$item = [Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([GameItemElement])
$item.modifiedState = [TagSystem]::new()
foreach ($pair in @(@('BASE_PROCESSING_SPEED_TAG',6),@('CURRENT_PROCESSING_SPEED_TAG',8),@('APPRAISAL_ITEM',1))) {
    $tag = [TagState]::new($pair[0],$pair[0])
    $tag.valueEnabled = $true
    $tag.valueInt = $pair[1]
    $item.modifiedState.dict.Add($pair[0],$tag)
}
$tp.GetField('_current',$flags).SetValue($null,$item)
$handTags = @{HAND_MATERIAL='CARBON_COMPOSITE';HAND_INTERFACE='NEURAL';HAND_CONDITION='NONE';HAND_ADDON_ARMORED='ARMORED';HAND_ADDON_BLADED='BLADED'}
$hand = $breakdown.GetMethod('ComputeHand',$flags).Invoke($null,@([Func[string,string]]{param($key) if ($handTags.ContainsKey($key)) { return $handTags[$key] } return $null}))
$breakdown.GetField('_memoResult',$flags).SetValue($null,$hand)
$breakdown.GetField('_memoItem',$flags).SetValue($null,$item)

function Render-All([int]$build) {
    $tp.GetField('_buildId',$flags).SetValue($null,$build)
    $breakdown.GetField('_memoBuild',$flags).SetValue($null,$build)
    $b = [RichTextBuilder]::new(40)
    [void]$provider.GetType('ItemDetails.MachineSpeedLine').GetMethod('Write',$flags).Invoke($null,@($b,$item,'Speed:'))
    $wr = [Activator]::CreateInstance($writerType,$rw,$null,@($b,$item,'value'),$null)
    [void]$entryType.GetMethod('TryAppendValue',$rw).Invoke($entryObject,@($wr,$item))
    [void]$writerType.GetMethod('Finish',$rw).Invoke($wr,@($false))
    return $b
}

$sectionApi.GetMethod('Enable',$flags).Invoke($null,@())
$watch = $probe::Watch()
$claim = $probe::ClaimAll()
try {
    Check ($probe::SectionAvailable()) 'section protocol advertised after enable'
    # Watch fills snapshots; claim keeps the offline host away from native Add.
    $first = Render-All 1
    $second = Render-All 2
    Check ($probe::Runs($first) -eq $probe::Runs($second)) 'section snapshots are deterministic across builds'
    Check ($probe::UsesStableIds($first)) "emitted ids match the SDK constants (actual: $($probe::Ids($first)))"
    Check ($probe::Runs($first).Contains('basePerDay=6') -and $probe::Runs($first).Contains('currentPerDay=8')) 'machine speed fields survive into the snapshot'
    Check ($probe::Runs($first).Contains('total=500')) 'appraisal total survives into the snapshot'
}
finally { $claim.Dispose(); $watch.Dispose(); $sectionApi.GetMethod('Disable',$flags).Invoke($null,@()) }
Check (!$probe::SectionAvailable()) 'section availability withdrawn on disable'
$queries = $provider.GetType('ItemDetails.LiquidQueries')
$queries.GetMethod('Enable',$flags).Invoke($null,@())
Check ($probe::LiquidAvailable()) 'liquid availability advertised independently'
$queries.GetMethod('Disable',$flags).Invoke($null,@())
Check (!$probe::LiquidAvailable()) 'liquid availability withdrawn independently'
