param(
    [string]$GamePath = "$env:APPDATA\Vintagestory",
    [string]$ModPath = "$env:APPDATA\VintagestoryData\Mods\AngelsShare_dev"
)

$ErrorActionPreference = "Stop"
$script:AssertionCount = 0

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (!$Condition) {
        throw $Message
    }

    $script:AssertionCount++
}

function Assert-Equal {
    param(
        [object]$Actual,
        [object]$Expected,
        [string]$Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected', received '$Actual'."
    }

    $script:AssertionCount++
}

function Assert-Close {
    param(
        [double]$Actual,
        [double]$Expected,
        [double]$Tolerance = 0.000001,
        [string]$Message
    )

    if ([Math]::Abs($Actual - $Expected) -gt $Tolerance) {
        throw "$Message Expected $Expected +/- $Tolerance, received $Actual."
    }

    $script:AssertionCount++
}

function Assert-Designation {
    param(
        [object]$Snapshot,
        [string]$Code,
        [Nullable[double]]$NumericValue,
        [string]$Message
    )

    $matches = @($Snapshot.Designations | Where-Object { $_.Code -eq $Code })
    Assert-True ($matches.Count -eq 1) $Message

    if ($null -ne $NumericValue) {
        Assert-Close $matches[0].NumericValue.Value $NumericValue.Value 0.000001 $Message
    }
}

function Invoke-MaturationScenario {
    param(
        [string]$LiquidCode,
        [double]$ActualElapsedHours,
        [double]$EffectiveMaturationHours,
        [double]$AverageTemperature,
        [double]$AverageRainfall,
        [object]$Cask,
        [object]$Loss = $null
    )

    if ($null -eq $Loss) {
        $Loss = [AngelsShare.MaturationLossInput]::new()
    }

    $input = [AngelsShare.MaturationCalculationInput]::new()
    $input.LiquidCode = $LiquidCode
    $input.TotalActualElapsedHours = $ActualElapsedHours
    $input.TotalEffectiveMaturationHours = $EffectiveMaturationHours
    $input.AverageTemperature = $AverageTemperature
    $input.AverageRainfall = $AverageRainfall
    $input.AverageHumidityModifier =
        [AngelsShare.MaturationMath]::GetHumidityModifier($AverageRainfall)
    $input.Cask = $Cask
    $input.Loss = $Loss

    return [AngelsShare.MaturationMath]::Calculate($input)
}

function Invoke-MaturationTimeline {
    param(
        [string]$LiquidCode,
        [object[]]$ClimatePeriods,
        [object]$Cask,
        [object]$Loss = $null
    )

    $actualHours = 0.0
    $effectiveHours = 0.0
    $temperatureHourIntegral = 0.0
    $rainfallHourIntegral = 0.0
    $humidityHourIntegral = 0.0

    foreach ($periodInput in $ClimatePeriods) {
        $period = [AngelsShare.MaturationMath]::CalculateClimatePeriod(
            $periodInput.Hours,
            $periodInput.Temperature,
            $periodInput.Rainfall,
            $Cask
        )
        $actualHours += $period.DurationHours
        $effectiveHours += $period.EffectiveMaturationHours
        $temperatureHourIntegral += $period.Temperature * $period.DurationHours
        $rainfallHourIntegral += $period.Rainfall * $period.DurationHours
        $humidityHourIntegral += $period.HumidityModifier * $period.DurationHours
    }

    if ($actualHours -le 0.0) {
        return Invoke-MaturationScenario `
            $LiquidCode 0.0 0.0 20.0 0.5 $Cask $Loss
    }

    $input = [AngelsShare.MaturationCalculationInput]::new()
    $input.LiquidCode = $LiquidCode
    $input.TotalActualElapsedHours = $actualHours
    $input.TotalEffectiveMaturationHours = $effectiveHours
    $input.AverageTemperature = $temperatureHourIntegral / $actualHours
    $input.AverageRainfall = $rainfallHourIntegral / $actualHours
    $input.AverageHumidityModifier = $humidityHourIntegral / $actualHours
    $input.Cask = $Cask
    $input.Loss = $null -eq $Loss `
        ? [AngelsShare.MaturationLossInput]::new() `
        : $Loss

    return [AngelsShare.MaturationMath]::Calculate($input)
}

[void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath "Lib\protobuf-net.dll"))
[void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath "VintagestoryAPI.dll"))
[void][Reflection.Assembly]::LoadFrom((Join-Path $GamePath "Mods\VSSurvivalMod.dll"))
[void][Reflection.Assembly]::LoadFrom((Join-Path $ModPath "AngelsShare.dll"))

# Lifecycle input/output routing remains explicit and independently testable.
$whiteSpiritItem = [Vintagestory.API.Common.Item]::new()
$whiteSpiritItem.Code = [Vintagestory.API.Common.AssetLocation]::new(
    "angels-share",
    "whitespiritportion-rye"
)
$whiteSpiritStack = [Vintagestory.API.Common.ItemStack]::new($whiteSpiritItem, 200)
$whiteSpiritOutput = $null
Assert-True (
    [AngelsShare.BarrelAgingUtil]::TryGetAgedOutputCode(
        $whiteSpiritStack,
        [ref]$whiteSpiritOutput
    )
) "White spirit did not resolve to an aged output."
Assert-Equal $whiteSpiritOutput.ToString() "angels-share:spiritportion-rye" `
    "White spirit resolved to the wrong aged output."

$ginItem = [Vintagestory.API.Common.Item]::new()
$ginItem.Code = [Vintagestory.API.Common.AssetLocation]::new(
    "angels-share",
    "ginportion-cassava"
)
$ginStack = [Vintagestory.API.Common.ItemStack]::new($ginItem, 200)
$ginOutput = $null
Assert-True (
    [AngelsShare.BarrelAgingUtil]::TryGetAgedOutputCode($ginStack, [ref]$ginOutput)
) "Gin could not begin another aging session."
Assert-Equal $ginOutput.ToString() "angels-share:ginportion-cassava" `
    "Gin re-aging changed its liquid identity."

$unsupportedItem = [Vintagestory.API.Common.Item]::new()
$unsupportedItem.Code = [Vintagestory.API.Common.AssetLocation]::new(
    "angels-share",
    "spiritportion-rye"
)
$unsupportedStack = [Vintagestory.API.Common.ItemStack]::new($unsupportedItem, 200)
$unsupportedOutput = $null
Assert-True (
    ![AngelsShare.BarrelAgingUtil]::TryGetAgedOutputCode(
        $unsupportedStack,
        [ref]$unsupportedOutput
    )
) "A finalized spirit was incorrectly treated as a supported aging input."
Assert-Close ([AngelsShare.BarrelAgingUtil]::MinimumAgingVolumeLitres) 2.0 0.0 `
    "The code minimum diverged from the continuous-aging recipe minimum."
Assert-True ([AngelsShare.BarrelQuickUnsealGesture]::IsQuickRelease(200)) `
    "A release at the quick-unseal boundary was treated as a hold."
Assert-True (![AngelsShare.BarrelQuickUnsealGesture]::IsQuickRelease(201)) `
    "A click-and-hold gesture was treated as a quick unseal."

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
$tooYoungDesignation = [AngelsShare.MaturationDesignation]::new()
$tooYoungDesignation.Code = "angels-share:age-stated"
$tooYoungDesignation.NumericValue = 6.0
$tooYoungDesignation.UnitCode = "years"
$record.ActiveSession.ProjectedOutcome.Designations.Add($tooYoungDesignation)
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
Assert-True (
    ![AngelsShare.MaturationRecordCodec]::HasDesignation(
        $roundTrip.ActiveSession.ProjectedOutcome,
        "angels-share:age-stated"
    )
) "Sub-eight-year product retained an age-stated designation."

$namedOutcome = [AngelsShare.MaturationOutcome]::new()
$proofDesignation = [AngelsShare.MaturationDesignation]::new()
$proofDesignation.Code = "angels-share:cask-strength"
$proofDesignation.NumericValue = 122.0
$proofDesignation.UnitCode = "proof"
$namedOutcome.Designations.Add($proofDesignation)
$ageDesignation = [AngelsShare.MaturationDesignation]::new()
$ageDesignation.Code = "angels-share:age-stated"
$ageDesignation.NumericValue = 8.0
$ageDesignation.UnitCode = "years"
$namedOutcome.Designations.Add($ageDesignation)
$displayName = [AngelsShare.AgingDisplayUtil]::FormatAgedSpiritDisplayName(
    "Rye Whiskey",
    $namedOutcome
)
Assert-True (
    $displayName -eq "122 Proof 8-Year Rye Whiskey"
) "Compact finalized name did not match the intended format."

$reserveOutcome = [AngelsShare.MaturationOutcome]::new()
$reserveOutcome.TierCode = "reserve"
$reserveName = [AngelsShare.AgingDisplayUtil]::FormatAgedSpiritDisplayName(
    "Rye Whiskey",
    $reserveOutcome
)
Assert-True (
    $reserveName -eq "Reserve Rye Whiskey"
) "Reserve product lost its tier in the compact name."

$overOakedOutcome = [AngelsShare.MaturationOutcome]::new()
$overOakedOutcome.TierCode = "over-oaked"
$overOakedName = [AngelsShare.AgingDisplayUtil]::FormatAgedSpiritDisplayName(
    "Rye Whiskey",
    $overOakedOutcome
)
Assert-True (
    $overOakedName -eq "Over-oaked Rye Whiskey"
) "Over-oaked product lost its tier in the compact name."

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

# A finalized product may begin a later session (resealing and gin re-aging),
# while its completed history and aggregate outcome remain available.
$reagingStack = [Vintagestory.API.Common.ItemStack]::new()
$reagingStack.Attributes = $stack.Attributes.Clone()
$reagingRecord = $finalRoundTrip
$reagingRecord.State = [AngelsShare.MaturationRecordState]::Active
$reagingRecord.ActiveSession = [AngelsShare.ActiveMaturationSession]::new()
$reagingRecord.ActiveSession.Sequence = 3
$reagingRecord.ActiveSession.InputLiquidCode = "angels-share:ginportion-cassava"
$reagingRecord.ActiveSession.SealedAtCalendarHours = 160.0
$reagingRecord.ActiveSession.LastIntegratedAtCalendarHours = 160.0

[AngelsShare.MaturationRecordCodec]::Write($reagingStack, $reagingRecord)
$reloadedReagingRecord = $null
Assert-True (
    [AngelsShare.MaturationRecordCodec]::TryRead(
        $reagingStack,
        [ref]$reloadedReagingRecord
    )
) "A resealed product did not survive save/reload serialization."
Assert-True (
    $reloadedReagingRecord.State -eq [AngelsShare.MaturationRecordState]::Active
) "A resealed product did not reload as active."
Assert-Equal $reloadedReagingRecord.ActiveSession.Sequence 3 `
    "A resealed product lost its new session sequence."
Assert-Equal $reloadedReagingRecord.CompletedSessions.Count 1 `
    "Beginning another session changed completed maturation history."
Assert-True ($null -ne $reloadedReagingRecord.FinalizedProduct) `
    "Beginning another session discarded the prior finalized product."

$legacyStack = [Vintagestory.API.Common.ItemStack]::new()
$legacyStack.Attributes = [Vintagestory.API.Datastructures.TreeAttribute]::new()
$legacy = $legacyStack.Attributes.GetOrAddTreeAttribute("maturationData")
$legacy.SetDouble("sealedAtTotalHours", 10.0)
$legacy.SetDouble("unsealedAtTotalHours", 34.0)
$legacy.SetDouble("ageHoursTotal", 24.0)
$legacy.SetDouble("ageHours", 30.0)
$legacy.SetBool("angelsshareAged", $true)
$legacy.SetString("ageTier", "reserve")
$legacy.SetDouble("maturityRatio", 1.2)
$legacy.SetDouble("overAgeRatio", 0.2)
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
    $migrated.FinalizedProduct.Outcome.TierCode -eq "reserve"
) "Legacy migration reclassified the stored tier from derived oak/extraction values."
Assert-True (
    $migrated.FinalizedProduct.Outcome.Oak -eq 100.0
) "Legacy over-age ratio was not mapped into the oak accumulator."
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

# Pure calculation boundary: climate and calendar units.
$baselineCask = [AngelsShare.MaturationMath]::CreateBaselineCaskProfile()
$temperatePeriod = [AngelsShare.MaturationMath]::CalculateClimatePeriod(
    24.0,
    20.0,
    0.5,
    $baselineCask
)
$hotDryPeriod = [AngelsShare.MaturationMath]::CalculateClimatePeriod(
    24.0,
    30.0,
    0.2,
    $baselineCask
)
$coolHumidPeriod = [AngelsShare.MaturationMath]::CalculateClimatePeriod(
    24.0,
    12.0,
    0.75,
    $baselineCask
)

Assert-Close $temperatePeriod.HumidityModifier 1.0 0.000001 `
    "Temperate humidity modifier changed unexpectedly."
Assert-Close $temperatePeriod.EffectiveMaturationHours 24.0 0.000001 `
    "Temperate effective maturation changed unexpectedly."
$constantClimateResult = [AngelsShare.MaturationMath]::CalculateConstantClimate(
    "angels-share:whitespiritportion-rye",
    24.0,
    20.0,
    0.5,
    $baselineCask,
    $null
)
Assert-Close $constantClimateResult.Snapshot.AgeHours 24.0 0.000001 `
    "Constant-climate convenience calculation lost effective maturation."
Assert-Close $constantClimateResult.Snapshot.AgeDays 1.0 0.000001 `
    "Constant-climate convenience calculation converted hours to days incorrectly."
Assert-True (
    $hotDryPeriod.EffectiveMaturationHours -gt $temperatePeriod.EffectiveMaturationHours
) "Hot, dry climate did not mature faster than temperate climate."
Assert-True (
    $coolHumidPeriod.EffectiveMaturationHours -lt $temperatePeriod.EffectiveMaturationHours
) "Cool, humid climate did not mature slower than temperate climate."
Assert-Close $hotDryPeriod.TargetCalendarDaysToPeak 6.2 0.000001 `
    "Hot, dry baseline calendar target moved away from roughly six days."
Assert-Close $coolHumidPeriod.TargetCalendarDaysToPeak 29.7 0.000001 `
    "Cool, humid baseline calendar target moved away from roughly thirty days."

$hotDryAtPeak = [AngelsShare.MaturationMath]::CalculateConstantClimate(
    "angels-share:whitespiritportion-corn",
    $hotDryPeriod.TargetCalendarDaysToPeak * 24.0,
    30.0,
    0.2,
    $baselineCask,
    $null
)
$coolHumidAtPeak = [AngelsShare.MaturationMath]::CalculateConstantClimate(
    "angels-share:whitespiritportion-rye",
    $coolHumidPeriod.TargetCalendarDaysToPeak * 24.0,
    12.0,
    0.75,
    $baselineCask,
    $null
)
Assert-Close $hotDryAtPeak.Snapshot.MaturityRatio 1.0 0.000001 `
    "Hot, dry baseline no longer peaks at its calendar-time target."
Assert-Close $coolHumidAtPeak.Snapshot.MaturityRatio 1.0 0.000001 `
    "Cool, humid baseline no longer peaks at its calendar-time target."

$slowLuckyCask = [AngelsShare.MaturationMath]::CreateBaselineCaskProfile()
$slowLuckyCask.CaskVariance = 0.92
$slowLuckyCask.SafeWindowMultiplier = 1.35
$slowLuckyPeakDays = `
    $hotDryPeriod.TargetCalendarDaysToPeak `
    * $slowLuckyCask.SafeWindowMultiplier `
    / $slowLuckyCask.CaskVariance
$slowLuckyAtPeak = [AngelsShare.MaturationMath]::CalculateConstantClimate(
    "angels-share:whitespiritportion-rye",
    $slowLuckyPeakDays * 24.0,
    30.0,
    0.2,
    $slowLuckyCask,
    $null
)
Assert-True ($slowLuckyPeakDays -gt $hotDryPeriod.TargetCalendarDaysToPeak) `
    "Cask luck no longer varies the baseline calendar-time estimate."
Assert-Close $slowLuckyAtPeak.Snapshot.MaturityRatio 1.0 0.000001 `
    "Cask rate and safe-window luck no longer compose into calendar peak timing."
Assert-Close (
    [AngelsShare.MaturationMath]::ConvertCalendarHoursToDays(240.0)
) 10.0 0.000001 "Calendar-hour conversion regressed to the old 24x climate bug."

$mixedClimateTimeline = @(
    [pscustomobject]@{ Hours = 120.0; Temperature = 30.0; Rainfall = 0.2 },
    [pscustomobject]@{ Hours = 120.0; Temperature = 10.0; Rainfall = 0.8 }
)
$coldWetPeriod = [AngelsShare.MaturationMath]::CalculateClimatePeriod(
    24.0,
    10.0,
    0.8,
    $baselineCask
)
$mixedClimateResult = Invoke-MaturationTimeline `
    "angels-share:whitespiritportion-rye" `
    $mixedClimateTimeline `
    $baselineCask
Assert-Close $mixedClimateResult.Snapshot.TotalHours 240.0 0.000001 `
    "Multi-period climate timeline lost actual elapsed hours."
Assert-Close $mixedClimateResult.Snapshot.AverageTemperature 20.0 0.000001 `
    "Multi-period climate timeline averaged temperature incorrectly."
Assert-Close $mixedClimateResult.Snapshot.AverageRainfall 0.5 0.000001 `
    "Multi-period climate timeline averaged rainfall incorrectly."
Assert-Close $mixedClimateResult.Snapshot.AverageHumidityModifier (
    ($hotDryPeriod.HumidityModifier + $coldWetPeriod.HumidityModifier) / 2.0
) 0.000001 `
    "Multi-period climate timeline averaged humidity incorrectly."
Assert-Close $mixedClimateResult.Snapshot.AgeHours (
    $hotDryPeriod.EffectiveMaturationHours * 5.0 +
    $coldWetPeriod.EffectiveMaturationHours * 5.0
) 0.000001 "Multi-period climate timeline integrated effective aging incorrectly."

# Pure cask seed/profile boundary.
$caskSeed = [AngelsShare.MaturationMath]::MakeCaskSeed(
    12,
    64,
    -9,
    123.5,
    100,
    "angels-share:whitespiritportion-rye"
)
$sameCaskSeed = [AngelsShare.MaturationMath]::MakeCaskSeed(
    12,
    64,
    -9,
    123.5,
    100,
    "angels-share:whitespiritportion-rye"
)
$adjacentCaskSeed = [AngelsShare.MaturationMath]::MakeCaskSeed(
    13,
    64,
    -9,
    123.5,
    100,
    "angels-share:whitespiritportion-rye"
)
$rolledCask = [AngelsShare.MaturationMath]::RollCaskProfile($caskSeed)
$rerolledCask = [AngelsShare.MaturationMath]::RollCaskProfile($caskSeed)

Assert-Equal $sameCaskSeed $caskSeed "Identical cask inputs produced different seeds."
Assert-Equal $caskSeed 1805617265 "The numeric cask seed algorithm changed."
Assert-True ($adjacentCaskSeed -ne $caskSeed) `
    "Changing barrel position did not change the cask seed."
Assert-Equal $rerolledCask.Trait $rolledCask.Trait `
    "A cask seed did not reproduce its trait."
Assert-Equal $rolledCask.Trait "standard" `
    "Pinned cask seed changed its selected profile."
Assert-Close $rerolledCask.CaskVariance $rolledCask.CaskVariance 0.000000000001 `
    "A cask seed did not reproduce its maturation variance."
Assert-Close $rerolledCask.QualityBonus $rolledCask.QualityBonus 0.000000000001 `
    "A cask seed did not reproduce its quality bonus."
Assert-Close $rolledCask.CaskVariance 1.01324440622388 0.000000000001 `
    "Pinned standard cask variance changed."
Assert-Close $rolledCask.QualityBonus -0.587993219302964 0.000000000001 `
    "Pinned standard cask quality bonus changed."

$caskTraitCases = @(
    [pscustomobject]@{ Seed = 0; Trait = "standard" },
    [pscustomobject]@{ Seed = 1; Trait = "expressive" },
    [pscustomobject]@{ Seed = 14; Trait = "tight-grain" },
    [pscustomobject]@{ Seed = 16; Trait = "wide-grain" },
    [pscustomobject]@{ Seed = 18; Trait = "gentle" },
    [pscustomobject]@{ Seed = 35; Trait = "unicorn" },
    [pscustomobject]@{ Seed = 146; Trait = "flawed" }
)

foreach ($case in $caskTraitCases) {
    $profile = [AngelsShare.MaturationMath]::RollCaskProfile($case.Seed)
    Assert-Equal $profile.Trait $case.Trait `
        "Cask trait boundary changed for seed $($case.Seed)."
}

# Timing thresholds stay independently testable from item/block APIs.
Assert-Equal (
    [AngelsShare.MaturationMath]::GetMaturationDescriptor(0.049999)
) "Raw" "Raw/resting timing boundary changed."
Assert-Equal (
    [AngelsShare.MaturationMath]::GetMaturationDescriptor(0.05)
) "Resting" "Resting timing boundary changed."
Assert-Equal (
    [AngelsShare.MaturationMath]::GetMaturationDescriptor(0.55)
) "Maturing" "Maturing timing boundary changed."
Assert-Equal (
    [AngelsShare.MaturationMath]::GetMaturationDescriptor(0.98)
) "At the Edge" "Peak timing boundary changed."
Assert-Equal (
    [AngelsShare.MaturationMath]::GetMaturationDescriptor(1.12)
) "Over-Oaked" "Over-oaked timing boundary changed."
Assert-Equal (
    [AngelsShare.MaturationMath]::GetAgeTierFromMaturity(
        $true,
        0.82,
        90.0,
        80.0,
        50.0
    )
) "reserve" "Gin reserve timing threshold changed."
Assert-Equal (
    [AngelsShare.MaturationMath]::GetAgeTierFromMaturity(
        $false,
        0.82,
        90.0,
        80.0,
        50.0
    )
) "aged" "Spirit reserve timing threshold changed."
Assert-Equal (
    [AngelsShare.MaturationMath]::GetAgeTierFromMaturity(
        $false,
        1.05,
        90.0,
        60.0,
        70.0
    )
) "reserve" "Grace-window product was prematurely classified as over-oaked."
Assert-Equal (
    [AngelsShare.MaturationMath]::GetAgeTierFromMaturity(
        $false,
        1.09,
        90.0,
        60.0,
        70.0
    )
) "over-oaked" "Product outside the grace window avoided over-oaked classification."

# Representative outcome cases are data-only inputs. When balance formulas are
# intentionally revised, these expected baselines are the focused values to audit.
$calculationCases = @(
    [pscustomobject]@{
        Name = "Temperate near-peak standard cask"
        LiquidCode = "angels-share:whitespiritportion-rye"
        ActualHours = 410.4
        EffectiveHours = 18.0 * 0.95 * 24.0
        Temperature = 20.0
        Rainfall = 0.5
        Cask = $baselineCask
        ExpectedSafeWindow = 18.0
        ExpectedMaturity = 0.95
        ExpectedQuality = 100.0
        ExpectedIntensity = 53.75
        ExpectedSmoothness = 48.75
        ExpectedTier = "aged"
        ExpectedSpecial = ""
        ExpectedDesignationCodes = @()
    },
    [pscustomobject]@{
        Name = "Hot-dry near-peak standard cask"
        LiquidCode = "angels-share:whitespiritportion-corn"
        ActualHours = 6.2 * 0.95 * 24.0
        Temperature = 30.0
        Rainfall = 0.2
        Cask = $baselineCask
        ExpectedSafeWindow = 18.0
        ExpectedMaturity = 0.95
        ExpectedQuality = 100.0
        ExpectedIntensity = 100.0
        ExpectedSmoothness = 33.75
        ExpectedTier = "reserve"
        ExpectedSpecial = "Cask-Strength Reserve"
        ExpectedDesignationCodes = @("angels-share:cask-strength")
    },
    [pscustomobject]@{
        Name = "Cool-humid near-peak standard cask"
        LiquidCode = "angels-share:whitespiritportion-rye"
        ActualHours = 29.7 * 0.95 * 24.0
        Temperature = 12.0
        Rainfall = 0.75
        Cask = $baselineCask
        ExpectedSafeWindow = 18.0
        ExpectedMaturity = 0.95
        ExpectedQuality = 100.0
        ExpectedIntensity = 33.75
        ExpectedSmoothness = 87.75
        ExpectedTier = "reserve"
        ExpectedSpecial = "16-Year Old Reserve"
        ExpectedDesignationCodes = @("angels-share:age-stated")
    }
)

foreach ($case in $calculationCases) {
    $calculation = [AngelsShare.MaturationMath]::CalculateConstantClimate(
        $case.LiquidCode,
        $case.ActualHours,
        $case.Temperature,
        $case.Rainfall,
        $case.Cask,
        $null
    )
    $snapshot = $calculation.Snapshot

    Assert-Close $snapshot.SafeWindowDays $case.ExpectedSafeWindow 0.000001 `
        "$($case.Name): safe window changed."
    Assert-Close $snapshot.MaturityRatio $case.ExpectedMaturity 0.000001 `
        "$($case.Name): maturity ratio changed."
    Assert-Close $snapshot.Quality $case.ExpectedQuality 0.000001 `
        "$($case.Name): quality changed."
    Assert-Close $snapshot.Intensity $case.ExpectedIntensity 0.000001 `
        "$($case.Name): intensity changed."
    Assert-Close $snapshot.Smoothness $case.ExpectedSmoothness 0.000001 `
        "$($case.Name): smoothness changed."
    Assert-Equal $snapshot.Tier $case.ExpectedTier `
        "$($case.Name): tier changed."
    Assert-Equal $snapshot.SpecialStyle $case.ExpectedSpecial `
        "$($case.Name): designation label changed."

    $actualDesignationCodes = @($snapshot.Designations | ForEach-Object { $_.Code })
    Assert-Equal (
        $actualDesignationCodes -join ","
    ) ($case.ExpectedDesignationCodes -join ",") `
        "$($case.Name): structured designation set changed."
}

$hotDryResult = [AngelsShare.MaturationMath]::CalculateConstantClimate(
    "angels-share:whitespiritportion-corn",
    6.2 * 0.95 * 24.0,
    30.0,
    0.2,
    $baselineCask,
    $null
)
Assert-Designation $hotDryResult.Snapshot "angels-share:cask-strength" 155.0 `
    "Cask-strength proof designation changed."

$coolHumidResult = [AngelsShare.MaturationMath]::CalculateConstantClimate(
    "angels-share:whitespiritportion-rye",
    29.7 * 0.95 * 24.0,
    12.0,
    0.75,
    $baselineCask,
    $null
)
Assert-Designation $coolHumidResult.Snapshot "angels-share:age-stated" 16.0 `
    "Age-stated year designation changed."

$structuredOutcome = [AngelsShare.MaturationRecordCodec]::CreateOutcome(
    $hotDryResult.Snapshot
)
Assert-True (
    [AngelsShare.MaturationRecordCodec]::HasDesignation(
        $structuredOutcome,
        "angels-share:cask-strength"
    )
) "Pure cask-strength result was not carried into the versioned schema."

# Loss accounting is calculated without assuming the future evaporation formula.
# A zero conservation error means all observed volume change has a named cause.
$lossInput = [AngelsShare.MaturationLossInput]::new()
$lossInput.StartingVolumeLitres = 10.0
$lossInput.CurrentVolumeLitres = 9.4
$lossInput.AngelsShareLostLitres = 0.4
$lossInput.WhiskeyThiefSampledLitres = 0.1
$lossInput.OtherLossLitres = 0.1
$lossInput.FractionalAngelsShareRemainderLitres = 0.004
$loss = [AngelsShare.MaturationMath]::CalculateLoss($lossInput)

Assert-Close $loss.TotalVolumeChangeLitres 0.6 0.000001 `
    "Total volume loss was calculated incorrectly."
Assert-Close $loss.AccountedLossLitres 0.6 0.000001 `
    "Named loss categories were summed incorrectly."
Assert-Close $loss.ConservationErrorLitres 0.0 0.000001 `
    "Conserved loss inputs produced a conservation error."
Assert-Close $loss.RemainingVolumeFraction 0.94 0.000001 `
    "Remaining volume fraction was calculated incorrectly."
Assert-Close $loss.AngelsShareLossFraction 0.04 0.000001 `
    "Angel's-share loss fraction was calculated incorrectly."
Assert-Close $loss.FractionalAngelsShareRemainderLitres 0.004 0.000001 `
    "Fractional angel's-share remainder was discarded."

$unaccountedLossInput = [AngelsShare.MaturationLossInput]::new()
$unaccountedLossInput.StartingVolumeLitres = 10.0
$unaccountedLossInput.CurrentVolumeLitres = 9.5
$unaccountedLossInput.AngelsShareLostLitres = 0.4
$unaccountedLoss = [AngelsShare.MaturationMath]::CalculateLoss($unaccountedLossInput)
Assert-Close $unaccountedLoss.ConservationErrorLitres 0.1 0.000001 `
    "Unaccounted volume change was not exposed by the loss result."

Write-Host (
    "Maturation schema and pure calculation harness passed {0} assertions across climate, cask, timing, quality, tier, designation, and loss behavior." -f `
        $script:AssertionCount
)
