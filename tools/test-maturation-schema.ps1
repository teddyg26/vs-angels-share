param(
    [string]$GamePath = "$env:APPDATA\Vintagestory",
    [string]$ModPath = "$env:APPDATA\VintagestoryData\Mods\AngelsShare_dev"
)

$ErrorActionPreference = "Stop"

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (!$Condition) {
        throw $Message
    }
}

[void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath "VintagestoryAPI.dll"))
[void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath "Mods\VSSurvivalMod.dll"))
[void][Reflection.Assembly]::LoadFrom((Join-Path $ModPath "AngelsShare.dll"))

$stack = [Vintagestory.API.Common.ItemStack]::new()
$stack.Attributes = [Vintagestory.API.Datastructures.TreeAttribute]::new()

$record = [AngelsShare.MaturationRecord]::new()
$record.State = [AngelsShare.MaturationRecordState]::Active
$record.ActiveSession = [AngelsShare.ActiveMaturationSession]::new()
$record.ActiveSession.Sequence = 2
$record.ActiveSession.SealedAtCalendarHours = 123.5
$record.ActiveSession.LastIntegratedAtCalendarHours = 130.0
$record.ActiveSession.ActualElapsedHours = 6.5
$record.ActiveSession.EffectiveMaturationHours = 7.25
$record.ActiveSession.Volume.StartingVolumeLitres = 10.0
$record.ActiveSession.Volume.CurrentVolumeLitres = 9.75
$record.ActiveSession.Volume.FractionalAngelsShareRemainderLitres = 0.004
$record.ActiveSession.WhiskeyThief.SampleCount = 3
$record.ActiveSession.WhiskeyThief.Exposure = 1.25
$record.ActiveSession.Cask.ProfileCode = "angels-share:tight-grain"
$record.ActiveSession.Cask.RollSeed = 42
$record.ActiveSession.ProjectedOutcome.Quality = 81.5
$record.ActiveSession.ProjectedOutcome.Extraction = 75.0
$record.ActiveSession.ProjectedOutcome.Oak = 12.0
$record.Extensions.SetString("test:future", "preserved")

[AngelsShare.MaturationRecordCodec]::Write($stack, $record)

$roundTrip = $null
$read = [AngelsShare.MaturationRecordCodec]::TryRead($stack, [ref]$roundTrip)

Assert-True $read "Version 1 active record did not deserialize."
Assert-True ($roundTrip.SchemaVersion -eq 1) "Schema version did not round-trip."
Assert-True (
    $roundTrip.State -eq [AngelsShare.MaturationRecordState]::Active
) "Record state did not round-trip."
Assert-True ($roundTrip.ActiveSession.Sequence -eq 2) "Session sequence did not round-trip."
Assert-True (
    $roundTrip.ActiveSession.WhiskeyThief.SampleCount -eq 3
) "Whiskey Thief sample count did not round-trip."
Assert-True (
    [Math]::Abs(
        $roundTrip.ActiveSession.Volume.FractionalAngelsShareRemainderLitres - 0.004
    ) -lt 0.000001
) "Angel's share remainder did not round-trip."
Assert-True (
    $roundTrip.Extensions.GetString("test:future", "") -eq "preserved"
) "Extension data did not round-trip."

$completed = [AngelsShare.CompletedMaturationSession]::new()
$completed.Sequence = 2
$completed.SealedAtCalendarHours = 123.5
$completed.UnsealedAtCalendarHours = 140.0
$completed.ActualElapsedHours = 16.5
$completed.EffectiveMaturationHours = 18.0
$completed.Outcome = $roundTrip.ActiveSession.ProjectedOutcome
$roundTrip.CompletedSessions.Add($completed)
$roundTrip.FinalizedProduct = [AngelsShare.FinalizedMaturationProduct]::new()
$roundTrip.FinalizedProduct.CompletedSessionCount = 1
$roundTrip.FinalizedProduct.FinalizedAtCalendarHours = 140.0
$roundTrip.FinalizedProduct.TotalActualElapsedHours = 16.5
$roundTrip.FinalizedProduct.TotalEffectiveMaturationHours = 18.0
$roundTrip.FinalizedProduct.Outcome = $roundTrip.ActiveSession.ProjectedOutcome
$roundTrip.ActiveSession = $null
$roundTrip.State = [AngelsShare.MaturationRecordState]::Finalized

[AngelsShare.MaturationRecordCodec]::Write($stack, $roundTrip)
$finalRoundTrip = $null
$finalRead = [AngelsShare.MaturationRecordCodec]::TryRead($stack, [ref]$finalRoundTrip)

Assert-True $finalRead "Version 1 finalized record did not deserialize."
Assert-True (
    $finalRoundTrip.State -eq [AngelsShare.MaturationRecordState]::Finalized
) "Finalized state did not round-trip."
Assert-True (
    $null -eq $finalRoundTrip.ActiveSession
) "Finalized record retained an active session."
Assert-True (
    $finalRoundTrip.CompletedSessions[0].UnsealedAtCalendarHours -eq 140.0
) "Closed session did not retain its unseal time."

$finalTree = $stack.Attributes.GetTreeAttribute("maturationData")
Assert-True (
    $null -eq $finalTree.GetTreeAttribute("activeSession")
) "Serialized finalized data retained the active-session subtree."

$legacyStack = [Vintagestory.API.Common.ItemStack]::new()
$legacyStack.Attributes = [Vintagestory.API.Datastructures.TreeAttribute]::new()
$legacy = $legacyStack.Attributes.GetOrAddTreeAttribute("maturationData")
$legacy.SetDouble("sealedAtTotalHours", 10.0)
$legacy.SetDouble("unsealedAtTotalHours", 34.0)
$legacy.SetDouble("ageHoursTotal", 24.0)
$legacy.SetDouble("ageHours", 30.0)
$legacy.SetBool("angelsshareAged", $true)
$legacy.SetString("ageTier", "reserve")
$legacy.SetString("specialStyle", "Age-Stated Cask-Strength Reserve")
$legacy.SetDouble("proof", 125.0)
$legacy.SetDouble("ageStatementYears", 12.0)

$migrated = $null
$legacyRead = [AngelsShare.MaturationRecordCodec]::TryRead(
    $legacyStack,
    [ref]$migrated
)

Assert-True $legacyRead "Legacy maturation record did not migrate."
Assert-True (
    $migrated.State -eq [AngelsShare.MaturationRecordState]::Finalized
) "Legacy finalized state was not recognized."
Assert-True ($null -eq $migrated.ActiveSession) "Migrated final retained an active session."
Assert-True ($migrated.CompletedSessions.Count -eq 1) "Migrated history is incomplete."
Assert-True (
    [AngelsShare.MaturationRecordCodec]::HasDesignation(
        $migrated.FinalizedProduct.Outcome,
        "angels-share:age-stated"
    )
) "Age-stated designation was not migrated."
Assert-True (
    [AngelsShare.MaturationRecordCodec]::HasDesignation(
        $migrated.FinalizedProduct.Outcome,
        "angels-share:cask-strength"
    )
) "Cask-strength designation was not migrated."

$futureStack = [Vintagestory.API.Common.ItemStack]::new()
$futureStack.Attributes = [Vintagestory.API.Datastructures.TreeAttribute]::new()
$futureTree = $futureStack.Attributes.GetOrAddTreeAttribute("maturationData")
$futureTree.SetInt("schemaVersion", 999)
$futureRecord = $null

Assert-True (
    ![AngelsShare.MaturationRecordCodec]::TryRead($futureStack, [ref]$futureRecord)
) "Unsupported future schema was accepted."
Assert-True (
    [AngelsShare.MaturationRecordCodec]::HasStoredRecord($futureStack)
) "Unsupported future schema was not preserved as stored data."

Write-Host "Maturation schema round-trip, migration, and version checks passed."
