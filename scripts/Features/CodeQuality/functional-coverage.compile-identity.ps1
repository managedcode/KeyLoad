$script:FcCompileIdentity = [ordered]@{
    MetadataPrefix = 'KeyLoad.FunctionalCompileIdentity.'
    VersionKey = 'KeyLoad.FunctionalCompileIdentity.Version'
    SourceCountKey = 'KeyLoad.FunctionalCompileIdentity.SourceCount'
    CentralCountKey = 'KeyLoad.FunctionalCompileIdentity.CentralCount'
    ProducerKey = 'KeyLoad.FunctionalCompileIdentity.Producer'
    SourceKey = 'KeyLoad.FunctionalCompileIdentity.Source'
    CentralKey = 'KeyLoad.FunctionalCompileIdentity.Central'
    Version = '1'
    MaximumAttributes = 5012
    MaximumValueCharacters = 8192
    MaximumReceiptCharacters = 33554432
    InvalidReceipt = 'The native UnitTests compilation receipt is missing, duplicated, malformed or unsupported.'
}

function Read-FcAssemblyMetadataStrings([object] $Reader) {
    $records = [Collections.Generic.List[object]]::new()
    $totalCharacters = 0L
    foreach ($handle in $Reader.CustomAttributes) {
        $attribute = $Reader.GetCustomAttribute($handle)
        if ($attribute.Parent.Kind -ne [Reflection.Metadata.HandleKind]::AssemblyDefinition -or
            $attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
        $member = $Reader.GetMemberReference([Reflection.Metadata.MemberReferenceHandle] $attribute.Constructor)
        if ($Reader.GetString($member.Name) -cne '.ctor' -or
            $member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
        $type = $Reader.GetTypeReference([Reflection.Metadata.TypeReferenceHandle] $member.Parent)
        if ($Reader.GetString($type.Namespace) -cne 'System.Reflection' -or
            $Reader.GetString($type.Name) -cne 'AssemblyMetadataAttribute') { continue }
        if ($type.ResolutionScope.Kind -ne [Reflection.Metadata.HandleKind]::AssemblyReference -or
            $Reader.GetString($Reader.GetAssemblyReference([Reflection.Metadata.AssemblyReferenceHandle] $type.ResolutionScope).Name) -cne 'System.Runtime') {
            throw $script:FcCompileIdentity.InvalidReceipt
        }
        $signature = [byte[]] $Reader.GetBlobBytes($member.Signature)
        if ($signature.Length -ne 5 -or $signature[0] -ne 0x20 -or $signature[1] -ne 2 -or
            $signature[2] -ne 1 -or $signature[3] -ne 0x0e -or $signature[4] -ne 0x0e) {
            throw $script:FcCompileIdentity.InvalidReceipt
        }
        if ($records.Count -ge $script:FcCompileIdentity.MaximumAttributes) { throw $script:FcCompileIdentity.InvalidReceipt }
        $blob = $Reader.GetBlobReader($attribute.Value)
        if ($blob.ReadUInt16() -ne 1) { throw $script:FcCompileIdentity.InvalidReceipt }
        $key = $blob.ReadSerializedString()
        $value = $blob.ReadSerializedString()
        if ($blob.ReadUInt16() -ne 0 -or $blob.RemainingBytes -ne 0 -or
            [string]::IsNullOrEmpty($key) -or $key.Length -gt $script:FcCompileIdentity.MaximumValueCharacters -or
            $null -eq $value -or $value.Length -gt $script:FcCompileIdentity.MaximumValueCharacters) {
            throw $script:FcCompileIdentity.InvalidReceipt
        }
        if ($key.StartsWith($script:FcCompileIdentity.MetadataPrefix, [StringComparison]::Ordinal)) {
            if ($totalCharacters -gt $script:FcCompileIdentity.MaximumReceiptCharacters - $key.Length - $value.Length) {
                throw $script:FcCompileIdentity.InvalidReceipt
            }
            $totalCharacters += $key.Length + $value.Length
            $records.Add([ordered]@{ key = $key; value = $value })
        }
    }
    @($records.ToArray())
}

function Read-FcCompileReceiptEntries([object[]] $Records, [string] $Key) {
    @($Records | Where-Object { $_.key -ceq $Key } | ForEach-Object {
        $separator = ([string] $_.value).LastIndexOf('|')
        if ($separator -le 0 -or $separator -eq $_.value.Length - 1) { throw $script:FcCompileIdentity.InvalidReceipt }
        $path = ([string] $_.value).Substring(0, $separator)
        $hash = ([string] $_.value).Substring($separator + 1)
        if ($path.Length -gt 4096 -or $path.Contains('\') -or $path.Contains('|') -or
            $path -match '[\x00-\x1f\x7f]' -or $path.StartsWith('/', [StringComparison]::Ordinal) -or
            $path.Split('/') -contains '..' -or $path.Split('/') -contains '.' -or
            $hash -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcCompileIdentity.InvalidReceipt }
        [ordered]@{ path = $path; sha256 = $hash }
    })
}

function Assert-FcCompileReceiptEntries([object[]] $Actual, [object[]] $Expected) {
    if ($Actual.Count -ne $Expected.Count) { throw $script:FcCompileIdentity.InvalidReceipt }
    $actualByPath = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Actual) {
        if ($actualByPath.ContainsKey([string] $entry.path)) { throw $script:FcCompileIdentity.InvalidReceipt }
        $actualByPath.Add([string] $entry.path, [string] $entry.sha256)
    }
    foreach ($entry in $Expected) {
        if (-not $actualByPath.ContainsKey([string] $entry.path) -or
            $actualByPath[[string] $entry.path] -cne [string] $entry.sha256) {
            throw $script:FcCompileIdentity.InvalidReceipt
        }
    }
}

function Read-FcUnitCompileReceipt([string] $DllPath, [object[]] $ExpectedSources, [object[]] $ExpectedCentralInputs,
    [object] $ExpectedProducer) {
    $stream = [IO.File]::OpenRead($DllPath)
    $pe = $null
    try {
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $records = @(Read-FcAssemblyMetadataStrings $reader)
        $allowedKeys = @($script:FcCompileIdentity.VersionKey,$script:FcCompileIdentity.SourceCountKey,
            $script:FcCompileIdentity.CentralCountKey,$script:FcCompileIdentity.SourceKey,
            $script:FcCompileIdentity.CentralKey,$script:FcCompileIdentity.ProducerKey)
        if (@($records | Where-Object { $_.key -cnotin $allowedKeys }).Count -ne 0) {
            throw $script:FcCompileIdentity.InvalidReceipt
        }
        $versions = @($records | Where-Object { $_.key -ceq $script:FcCompileIdentity.VersionKey })
        $sourceCounts = @($records | Where-Object { $_.key -ceq $script:FcCompileIdentity.SourceCountKey })
        $centralCounts = @($records | Where-Object { $_.key -ceq $script:FcCompileIdentity.CentralCountKey })
        $producerRows = @(Read-FcCompileReceiptEntries $records $script:FcCompileIdentity.ProducerKey)
        if ($versions.Count -ne 1 -or $versions[0].value -cne $script:FcCompileIdentity.Version -or
            $sourceCounts.Count -ne 1 -or $centralCounts.Count -ne 1 -or
            $sourceCounts[0].value -cnotmatch '\A[1-9][0-9]{0,3}\z' -or
            $centralCounts[0].value -cne '6' -or $producerRows.Count -ne 1) { throw $script:FcCompileIdentity.InvalidReceipt }
        $sources = @(Read-FcCompileReceiptEntries $records $script:FcCompileIdentity.SourceKey)
        $central = @(Read-FcCompileReceiptEntries $records $script:FcCompileIdentity.CentralKey)
        if ($sources.Count -ne [int] $sourceCounts[0].value -or $sources.Count -gt 5000 -or $central.Count -ne 6) {
            throw $script:FcCompileIdentity.InvalidReceipt
        }
        Assert-FcCompileReceiptEntries $sources $ExpectedSources
        Assert-FcCompileReceiptEntries $central $ExpectedCentralInputs
        Assert-FcCompileReceiptEntries $producerRows @($ExpectedProducer)
        $sourceMaterial = (($ExpectedSources | ForEach-Object { [string] $_.path + '|' + [string] $_.sha256 }) -join "`n") + "`n"
        $sourceSetSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sourceMaterial))).ToLowerInvariant()
        $serialized = ConvertTo-Json -InputObject ([ordered]@{ sourceSetSha256 = $sourceSetSha256; centralInputs = $ExpectedCentralInputs }) -Compress -Depth 5
        if ($serialized.Length -gt $script:FcCompileIdentity.MaximumReceiptCharacters) { throw $script:FcCompileIdentity.InvalidReceipt }
        [ordered]@{
            version = 1
            sourceCount = $ExpectedSources.Count
            sourceSetSha256 = $sourceSetSha256
            centralInputCount = $ExpectedCentralInputs.Count
            centralInputs = $ExpectedCentralInputs
            producer = $producerRows[0]
            binding = 'same-assembly native metadata; DLL MVID and portable-PDB CodeView identity verified'
        }
    }
    finally {
        if ($null -ne $pe) { $pe.Dispose() }
        $stream.Dispose()
    }
}
