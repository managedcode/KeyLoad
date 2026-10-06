$script:PsmProductContributors = [ordered]@{
    SchemaVersion = 1
    MinimumSuites = 4
    VersionProperty = 'schemaVersion'
    ContributorsProperty = 'contributors'
    Invalid = 'The source-controlled product contributor registry is invalid or incomplete.'
}

function Read-PsmProductContributorRegistry([string] $Root) {
    $relative = $script:Psm.ContributorRegistryPath
    $path = Resolve-FcPath $Root $relative
    $bytes = Read-PsmBounded $path
    $document = $null
    try {
        $options = [System.Text.Json.JsonDocumentOptions]::new()
        $options.MaxDepth = $script:Psm.MaximumJsonDepth
        $document = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($bytes), $options)
        Assert-PsmUniqueJsonProperties $document.RootElement 0
        $rootElement = $document.RootElement
        Assert-PsmJsonObjectKeys $rootElement @(
            $script:PsmProductContributors.VersionProperty,
            $script:PsmProductContributors.ContributorsProperty)
        $versionElement = $rootElement.GetProperty($script:PsmProductContributors.VersionProperty)
        if ($versionElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
            $versionElement.GetRawText() -cne [string]$script:PsmProductContributors.SchemaVersion) {
            throw $script:PsmProductContributors.Invalid
        }
        $rowsElement = $rootElement.GetProperty($script:PsmProductContributors.ContributorsProperty)
        if ($rowsElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or
            $rowsElement.GetArrayLength() -lt $script:PsmProductContributors.MinimumSuites -or
            $rowsElement.GetArrayLength() -gt $script:Psm.MaximumSources) {
            throw $script:PsmProductContributors.Invalid
        }
        $registry = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($bytes)) -AsHashtable `
            -Depth $script:Psm.MaximumJsonDepth
        if ($registry.schemaVersion -ne $script:PsmProductContributors.SchemaVersion -or
            $registry.contributors -isnot [array]) {
            throw $script:PsmProductContributors.Invalid
        }
        $candidate = [ordered]@{ contributors = [object[]]@($registry.contributors) }
        $bounds = [ordered]@{ maximumFiles = $script:Psm.MaximumSources }
        $null = Read-FcNativeContributors $candidate $bounds ([string[]]$script:Psm.ProductionModuleRoster)
        return [object[]]@($registry.contributors)
    }
    catch [System.Text.Json.JsonException] {
        throw $script:PsmProductContributors.Invalid
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }
}

function Assert-PsmUniqueJsonProperties([System.Text.Json.JsonElement] $Element, [int] $Depth) {
    if ($Depth -gt $script:Psm.MaximumJsonDepth) { throw $script:PsmProductContributors.Invalid }
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw $script:PsmProductContributors.Invalid }
            Assert-PsmUniqueJsonProperties $property.Value ($Depth + 1)
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($child in $Element.EnumerateArray()) {
            Assert-PsmUniqueJsonProperties $child ($Depth + 1)
        }
    }
}

function Assert-PsmJsonObjectKeys([System.Text.Json.JsonElement] $Element, [string[]] $Expected) {
    if ($Element.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        throw $script:PsmProductContributors.Invalid
    }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($property in $Element.EnumerateObject()) { [void]$names.Add($property.Name) }
    if ($names.Count -ne $Expected.Length) { throw $script:PsmProductContributors.Invalid }
    foreach ($name in $Expected) {
        if (-not $names.Contains($name)) { throw $script:PsmProductContributors.Invalid }
    }
}
