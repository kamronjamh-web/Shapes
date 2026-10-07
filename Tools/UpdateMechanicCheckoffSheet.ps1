$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$workbookPath = Join-Path $repoRoot 'Modulator_Remaining_Plan.xlsx'

if (-not (Test-Path $workbookPath)) {
    throw "Workbook not found: $workbookPath"
}

function Test-UnaddedMechanic {
    param(
        [string]$MechanicType
    )

    if ([string]::IsNullOrWhiteSpace($MechanicType)) {
        return $false
    }

    $tokens = @(
        'KnockbackMultiplier',
        'ChainCountAdd',
        'PullStrengthMultiplier',
        'CloneOrEchoPowerMultiplier',
        'New',
        'Behavioral Transformation',
        'Snapshot Stat Clamp',
        'Final Multiplicative Change',
        'Contribution Multiplicative Change'
    )

    foreach ($token in $tokens) {
        if ($MechanicType -like "*$token*") {
            return $true
        }
    }

    return $false
}

$excel = $null
$workbook = $null
$source = $null
$checkoff = $null

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false

    $workbook = $excel.Workbooks.Open($workbookPath)
    $source = $workbook.Worksheets.Item('Current_And_Proposed')

    $sheetName = 'Mechanic_Checkoff'
    foreach ($ws in $workbook.Worksheets) {
        if ($ws.Name -eq $sheetName) {
            $checkoff = $ws
            break
        }
    }

    if ($checkoff -eq $null) {
        $checkoff = $workbook.Worksheets.Add()
        $checkoff.Name = $sheetName
    }
    else {
        $checkoff.Cells.Clear()
    }

    $xlUp = -4162
    $xlValidateList = 3
    $xlValidAlertStop = 1
    $xlBetween = 1

    $lastSourceRow = $source.Cells($source.Rows.Count, 1).End($xlUp).Row
    $details = [System.Collections.Generic.List[object]]::new()
    $mechanicRows = [ordered]@{}

    for ($row = 2; $row -le $lastSourceRow; $row++) {
        $rowId = [string]$source.Cells.Item($row, 1).Text
        $catalogStatus = [string]$source.Cells.Item($row, 2).Text
        $tier = [string]$source.Cells.Item($row, 3).Text
        $name = [string]$source.Cells.Item($row, 4).Text
        $mechanicType = [string]$source.Cells.Item($row, 9).Text
        $baselineNumbers = [string]$source.Cells.Item($row, 10).Text
        $runtimeNotes = [string]$source.Cells.Item($row, 12).Text

        if ($catalogStatus -ne 'Proposed') {
            continue
        }

        if (-not (Test-UnaddedMechanic -MechanicType $mechanicType)) {
            continue
        }

        $details.Add([pscustomobject]@{
            RowId = $rowId
            Name = $name
            Tier = $tier
            CatalogStatus = $catalogStatus
            MechanicType = $mechanicType
            BaselineNumbers = $baselineNumbers
            RuntimeNotes = $runtimeNotes
        }) | Out-Null

        if (-not $mechanicRows.Contains($mechanicType)) {
            $mechanicRows[$mechanicType] = [System.Collections.Generic.List[string]]::new()
        }

        $mechanicRows[$mechanicType].Add($rowId)
    }

    $sortedMechanics = @($mechanicRows.Keys | Sort-Object)
    $sortedDetails = @($details | Sort-Object Tier, RowId)

    $checkoff.Cells.Item(1, 1).Value2 = 'Unadded Mechanics Checkoff'
    $checkoff.Cells.Item(2, 1).Value2 = 'Use [x] when a mechanic has been added and verified in code.'
    $checkoff.Cells.Item(3, 1).Value2 = 'Updated (local):'
    $checkoff.Cells.Item(3, 2).Value2 = (Get-Date).ToString('yyyy-MM-dd HH:mm')

    $summaryLabelRow = 4
    $checkoff.Cells.Item($summaryLabelRow, 1).Value2 = 'Mechanics total'
    $checkoff.Cells.Item($summaryLabelRow, 2).Value2 = [string]$sortedMechanics.Count
    $checkoff.Cells.Item($summaryLabelRow, 3).Value2 = 'Affected rows'
    $checkoff.Cells.Item($summaryLabelRow, 4).Value2 = [string]$sortedDetails.Count

    $mechanicHeaderRow = 6
    $mechanicHeaders = @(
        'Checkoff',
        'MechanicType',
        'ImplementedStatus',
        'VerifiedInCodeFiles',
        'DateChecked',
        'AffectedRows',
        'ExampleRowIds',
        'Notes'
    )

    for ($column = 0; $column -lt $mechanicHeaders.Count; $column++) {
        $checkoff.Cells.Item($mechanicHeaderRow, $column + 1).Value2 = $mechanicHeaders[$column]
    }

    $mechanicWriteRow = $mechanicHeaderRow + 1
    foreach ($mechanic in $sortedMechanics) {
        $rowIds = @($mechanicRows[$mechanic])
        $exampleRowIds = ($rowIds | Select-Object -First 8) -join ', '

        $checkoff.Cells.Item($mechanicWriteRow, 1).Value2 = '[ ]'
        $checkoff.Cells.Item($mechanicWriteRow, 2).Value2 = $mechanic
        $checkoff.Cells.Item($mechanicWriteRow, 3).Value2 = 'Not Added'
        $checkoff.Cells.Item($mechanicWriteRow, 4).Value2 = ''
        $checkoff.Cells.Item($mechanicWriteRow, 5).Value2 = ''
        $checkoff.Cells.Item($mechanicWriteRow, 6).Value2 = [string]$rowIds.Count
        $checkoff.Cells.Item($mechanicWriteRow, 7).Value2 = $exampleRowIds
        $checkoff.Cells.Item($mechanicWriteRow, 8).Value2 = ''
        $mechanicWriteRow++
    }

    $mechanicLastRow = $mechanicWriteRow - 1
    if ($mechanicLastRow -lt $mechanicHeaderRow) {
        $mechanicLastRow = $mechanicHeaderRow
    }

    $detailsTitleRow = $mechanicLastRow + 2
    $detailsHeaderRow = $detailsTitleRow + 1
    $checkoff.Cells.Item($detailsTitleRow, 1).Value2 = 'Rows Using Unadded Mechanics'

    $detailHeaders = @(
        'Checkoff',
        'RowId',
        'Name',
        'Tier',
        'CatalogStatus',
        'MechanicType',
        'HookStatus',
        'SuggestedBaselineNumbers',
        'RuntimeNotes',
        'CodeVerified'
    )

    for ($column = 0; $column -lt $detailHeaders.Count; $column++) {
        $checkoff.Cells.Item($detailsHeaderRow, $column + 1).Value2 = $detailHeaders[$column]
    }

    $detailWriteRow = $detailsHeaderRow + 1
    foreach ($row in $sortedDetails) {
        $checkoff.Cells.Item($detailWriteRow, 1).Value2 = '[ ]'
        $checkoff.Cells.Item($detailWriteRow, 2).Value2 = $row.RowId
        $checkoff.Cells.Item($detailWriteRow, 3).Value2 = $row.Name
        $checkoff.Cells.Item($detailWriteRow, 4).Value2 = $row.Tier
        $checkoff.Cells.Item($detailWriteRow, 5).Value2 = $row.CatalogStatus
        $checkoff.Cells.Item($detailWriteRow, 6).Value2 = $row.MechanicType
        $checkoff.Cells.Item($detailWriteRow, 7).Value2 = 'Needs Hook'
        $checkoff.Cells.Item($detailWriteRow, 8).Value2 = $row.BaselineNumbers
        $checkoff.Cells.Item($detailWriteRow, 9).Value2 = $row.RuntimeNotes
        $checkoff.Cells.Item($detailWriteRow, 10).Value2 = ''
        $detailWriteRow++
    }

    $detailLastRow = $detailWriteRow - 1
    if ($detailLastRow -lt $detailsHeaderRow) {
        $detailLastRow = $detailsHeaderRow
    }

    # Hidden helper list for dropdown validation.
    $checkoff.Cells.Item(1, 14).Value2 = 'Validation'
    $checkoff.Cells.Item(2, 14).Value2 = '[ ]'
    $checkoff.Cells.Item(3, 14).Value2 = '[x]'
    $checkoff.Cells.Item(5, 14).Value2 = 'Not Added'
    $checkoff.Cells.Item(6, 14).Value2 = 'In Progress'
    $checkoff.Cells.Item(7, 14).Value2 = 'Added'
    $checkoff.Cells.Item(8, 14).Value2 = 'Verified In Build'
    $checkoff.Columns.Item(14).Hidden = $true

    if ($mechanicLastRow -ge ($mechanicHeaderRow + 1)) {
        $mechanicCheckRange = $checkoff.Range("A$($mechanicHeaderRow + 1):A$mechanicLastRow")
        $mechanicCheckRange.Validation.Delete()
        $mechanicCheckRange.Validation.Add($xlValidateList, $xlValidAlertStop, $xlBetween, '=$N$2:$N$3')
        $mechanicCheckRange.Validation.IgnoreBlank = $true
        $mechanicCheckRange.Validation.InCellDropdown = $true

        $mechanicStatusRange = $checkoff.Range("C$($mechanicHeaderRow + 1):C$mechanicLastRow")
        $mechanicStatusRange.Validation.Delete()
        $mechanicStatusRange.Validation.Add($xlValidateList, $xlValidAlertStop, $xlBetween, '=$N$5:$N$8')
        $mechanicStatusRange.Validation.IgnoreBlank = $true
        $mechanicStatusRange.Validation.InCellDropdown = $true
    }

    if ($detailLastRow -ge ($detailsHeaderRow + 1)) {
        $detailCheckRange = $checkoff.Range("A$($detailsHeaderRow + 1):A$detailLastRow")
        $detailCheckRange.Validation.Delete()
        $detailCheckRange.Validation.Add($xlValidateList, $xlValidAlertStop, $xlBetween, '=$N$2:$N$3')
        $detailCheckRange.Validation.IgnoreBlank = $true
        $detailCheckRange.Validation.InCellDropdown = $true

        $detailStatusRange = $checkoff.Range("G$($detailsHeaderRow + 1):G$detailLastRow")
        $detailStatusRange.Validation.Delete()
        $detailStatusRange.Validation.Add($xlValidateList, $xlValidAlertStop, $xlBetween, '=$N$5:$N$8')
        $detailStatusRange.Validation.IgnoreBlank = $true
        $detailStatusRange.Validation.InCellDropdown = $true
    }

    $checkoff.Range("A$mechanicHeaderRow:H$mechanicHeaderRow").Font.Bold = $true
    $checkoff.Range("A$mechanicHeaderRow:H$mechanicHeaderRow").Interior.ColorIndex = 15
    $checkoff.Range("A$detailsHeaderRow:J$detailsHeaderRow").Font.Bold = $true
    $checkoff.Range("A$detailsHeaderRow:J$detailsHeaderRow").Interior.ColorIndex = 15
    $checkoff.Range('A1:A4').Font.Bold = $true

    $checkoff.Range("A$mechanicHeaderRow:H$mechanicLastRow").Borders.LineStyle = 1
    $checkoff.Range("A$detailsHeaderRow:J$detailLastRow").Borders.LineStyle = 1
    $checkoff.Range("A1:J$detailLastRow").WrapText = $true

    if ($mechanicLastRow -ge $mechanicHeaderRow) {
        $checkoff.Rows.Item($mechanicHeaderRow).AutoFilter() | Out-Null
    }

    if ($detailLastRow -ge $detailsHeaderRow) {
        $checkoff.Rows.Item($detailsHeaderRow).AutoFilter() | Out-Null
    }

    $checkoff.Columns('A:J').AutoFit() | Out-Null

    $workbook.Save()

    Write-Output "Updated workbook: $workbookPath"
    Write-Output "Mechanics tracked: $($sortedMechanics.Count)"
    Write-Output "Affected rows tracked: $($sortedDetails.Count)"
}
finally {
    if ($checkoff -ne $null) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($checkoff)
    }

    if ($source -ne $null) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($source)
    }

    if ($workbook -ne $null) {
        $workbook.Close($true)
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($workbook)
    }

    if ($excel -ne $null) {
        $excel.Quit()
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)
    }

    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}