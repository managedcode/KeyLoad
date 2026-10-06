$script:FcNativeContext = [ordered]@{ Invalid = 'An original native coverage context or terminal receipt is invalid.' }

function Assert-FcNativeContextBounds([object] $Actual, [object] $Expected) {
    Assert-FcNativeExactKeys $Actual @('maximumFiles','maximumTotalBytes','maximumFileBytes','maximumPathCharacters',
        'maximumManifestBytes','readBufferBytes','shutdownSeconds','settlementSeconds','maximumReportBytes')
    if ($Actual.maximumFiles -ne $Expected.maximumFiles -or $Actual.maximumTotalBytes -ne $Expected.maximumTotalBytes -or
        $Actual.maximumFileBytes -ne $Expected.maximumFileBytes -or $Actual.maximumPathCharacters -ne $Expected.maximumPathCharacters -or
        $Actual.maximumManifestBytes -ne $Expected.maximumManifestBytes -or $Actual.readBufferBytes -ne $Expected.readBufferBytes -or
        $Actual.maximumReportBytes -ne $Expected.maximumReportBytes -or
        $Actual.shutdownSeconds -ne $Expected.shutdownTimeoutSeconds -or
        $Actual.settlementSeconds -ne $Expected.settlementTimeoutSeconds) { throw $script:FcNativeContext.Invalid }
}

function Assert-FcNativeContext([object] $Context, [object] $Manifest, [object] $Bounds) {
    Assert-FcNativeExactKeys $Context @('schemaVersion','invocationId','producerSources','sourceTemplates','baseImage',
        'server','tool','bounds','files')
    if ($Context.schemaVersion -ne 1 -or $Context.invocationId -cnotmatch '\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z' -or
        $Context.files -isnot [array] -or $Context.files.Count -eq 0 -or
        $Context.files.Count -gt $Bounds.maximumFiles) { throw $script:FcNativeContext.Invalid }
    Assert-FcNativeContextBounds $Context.bounds $Bounds
    Assert-FcNativeContextTemplates $Context.sourceTemplates
    Assert-FcNativeContextOwners $Context $Manifest.value $Manifest.file.sha256
    $files = Read-FcNativeContextFiles $Context.files $Bounds
    Assert-FcNativeContextSourceTemplates $Context.sourceTemplates $files
    Assert-FcNativeContextProducerSources $Context.producerSources $files
    Assert-FcNativeContextModules $Context.server $Manifest.value $files
    Assert-FcNativeContextReceipts $Context $files
    $files
}

function Assert-FcNativeContextTool([object] $Files, [object] $ContextTool, [object] $Tool, [object] $Bounds, [string] $ToolPackageRoot) {
    $toolEntries = @($Files.Values | Where-Object { $_.path.StartsWith('tool/', [StringComparison]::Ordinal) -or
        $_.path.StartsWith('license/', [StringComparison]::Ordinal) })
    if ($toolEntries.Count -eq 0 -or $toolEntries.Count -gt $Bounds.maximumFiles) { throw $script:FcNativeContext.Invalid }
    foreach ($entry in $toolEntries) {
        if ($entry.path -cnotmatch '\A(?:tool|license)/[A-Za-z0-9._/-]+\z' -or $entry.path.Contains('..') -or
            $entry.length -le 0 -or $entry.length -gt $Bounds.maximumFileBytes -or
            $entry.mode -isnot [int] -or $entry.sha256 -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcNativeContext.Invalid }
    }
    $closure = Read-FcNativeToolClosure $ToolPackageRoot $Tool.version $Bounds $toolEntries
    if ($closure.nupkgSha512 -cne $Tool.nupkgSha512 -or $closure.closureDigest -cne $Tool.closureDigest -or
        $closure.packageFileCount + $closure.licenseFileCount -ne $toolEntries.Count -or
        $Tool.packageId -cne 'dotnet-coverage' -or $ContextTool.packageId -cne $Tool.packageId -or
        $ContextTool.version -cne $Tool.version -or $ContextTool.nupkgSha512 -cne $Tool.nupkgSha512 -or
        $ContextTool.nupkgSha256 -cne $closure.nupkgSha256 -or
        $ContextTool.closureDigest -cne $Tool.closureDigest) { throw $script:FcNativeContext.Invalid }
}

function Assert-FcNativeContextTemplates([object] $Templates) {
    Assert-FcNativeExactKeys $Templates @('dockerfileSha256','dockerfileMode','dockerignoreSha256','dockerignoreMode',
        'settingsSha256','wrapperSha256','wrapperMode','lifecycleSha256','lifecycleMode','targetSha256','targetMode')
    foreach ($name in @('dockerfileSha256','dockerignoreSha256','settingsSha256','wrapperSha256','lifecycleSha256','targetSha256')) {
        if ($Templates[$name] -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcNativeContext.Invalid }
    }
    foreach ($name in @('dockerfileMode','dockerignoreMode','wrapperMode','lifecycleMode','targetMode')) {
        if ($Templates[$name] -isnot [int] -or $Templates[$name] -lt 0) { throw $script:FcNativeContext.Invalid }
    }
}

function Assert-FcNativeContextOwners([object] $Context, [object] $SourceManifest, [string] $ManifestSha256) {
    Assert-FcNativeExactKeys $Context.baseImage @('reference','sourceReceiptSha256')
    Assert-FcNativeExactKeys $Context.server @('dllName','dllSha256','pdbName','pdbSha256','mvid','sourceReceiptSha256')
    Assert-FcNativeExactKeys $Context.tool @('packageId','version','repositoryCommit','nupkgSha256','nupkgSha512','closureDigest')
    if ($Context.baseImage.reference -cnotmatch '\A[^\s]{1,512}@sha256:[0-9a-f]{64}\z' -or
        $Context.baseImage.sourceReceiptSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Context.server.dllName -cne 'KeyLoad.Server.dll' -or $Context.server.pdbName -cne 'KeyLoad.Server.pdb' -or
        $Context.server.dllSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or $Context.server.pdbSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Context.server.mvid -cnotmatch '\A[0-9a-fA-F-]{36}\z' -or
        $Context.server.sourceReceiptSha256 -cne $ManifestSha256 -or
        $Context.tool.packageId -cne 'dotnet-coverage' -or $Context.tool.version -cne '18.11.2' -or
        $Context.tool.repositoryCommit -cnotmatch '\A[0-9a-f]{40}\z' -or
        $Context.tool.nupkgSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Context.tool.nupkgSha512 -cnotmatch '\A[A-Za-z0-9+/]{86}==$' -or
        $Context.tool.closureDigest -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcNativeContext.Invalid }
    $server = @($SourceManifest.compiledProducts | Where-Object module -ceq 'KeyLoad.Server')
    if ($server.Count -ne 1 -or $server[0].compiledIdentity.dllSha256 -cne $Context.server.dllSha256 -or
        $server[0].compiledIdentity.pdbSha256 -cne $Context.server.pdbSha256 -or
        $server[0].compiledIdentity.mvid -cne $Context.server.mvid) { throw $script:FcNativeContext.Invalid }
}

function Read-FcNativeContextFiles([object[]] $Entries, [object] $Bounds) {
    $files = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $total = 0L
    foreach ($entry in $Entries) {
        Assert-FcNativeExactKeys $entry @('path','mode','length','sha256')
        if ([string]::IsNullOrWhiteSpace($entry.path) -or $entry.path.Length -gt $Bounds.maximumPathCharacters -or
            $entry.path -cnotmatch '\A[A-Za-z0-9._/-]+\z' -or $entry.path.Split('/') -contains '..' -or
            $entry.path.Split('/') -contains '.' -or ($entry.mode -isnot [int] -and $entry.mode -isnot [long]) -or
            ($entry.mode -lt 0 -or $entry.mode -gt 511) -or
            ($entry.length -isnot [int] -and $entry.length -isnot [long]) -or $entry.length -le 0 -or
            $entry.length -gt $Bounds.maximumFileBytes -or $entry.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
            $total -gt $Bounds.maximumTotalBytes - $entry.length) { throw $script:FcNativeContext.Invalid }
        $files.Add([string] $entry.path, $entry)
        $total += $entry.length
    }
    $files
}

function Assert-FcNativeContextSourceTemplates([object] $Templates, [object] $Files) {
    $bound = @(
        @('Dockerfile','dockerfileSha256','dockerfileMode'), @('.dockerignore','dockerignoreSha256','dockerignoreMode'),
        @('settings.xml','settingsSha256',$null), @('server-wrapper.sh','wrapperSha256','wrapperMode'),
        @('server-lifecycle.sh','lifecycleSha256','lifecycleMode'), @('server-target.sh','targetSha256','targetMode'))
    foreach ($item in $bound) {
        $file = $Files[[string] $item[0]]
        $isDockerfile = $item[0] -ceq 'Dockerfile'
        if ($null -eq $file -or (-not $isDockerfile -and $file.sha256 -cne $Templates[[string] $item[1]]) -or
            ($isDockerfile -and $file.mode -ne $Templates.dockerfileMode) -or
            ($item[0] -ceq 'server-wrapper.sh' -and $file.mode -ne ($Templates.wrapperMode -bor 73)) -or
            ($item[0] -ceq 'server-target.sh' -and $file.mode -ne ($Templates.targetMode -bor 73)) -or
            ($item[0] -cin @('Dockerfile','.dockerignore','settings.xml','server-lifecycle.sh') -and
                $null -ne $item[2] -and $file.mode -ne $Templates[[string] $item[2]])) { throw $script:FcNativeContext.Invalid }
    }
    $dockerfileTemplate = $script:FcNativeMergeInput.ScriptHashes['functional-coverage.server-image.dockerfile']
    if ($dockerfileTemplate -cne $Templates.dockerfileSha256) { throw $script:FcNativeContext.Invalid }
    $sourceSettings = $script:FcNativeMergeInput.ScriptHashes['functional-coverage.server.settings.xml']
    if ($null -eq $sourceSettings -or $sourceSettings -cne $Templates.settingsSha256) { throw $script:FcNativeContext.Invalid }
}

function Assert-FcNativeContextProducerSources([object] $Sources, [object] $Files) {
    Assert-FcNativeExactKeys $Sources @('contracts','files','tool','materializer','entry')
    $sourceNames = [ordered]@{ contracts = 'functional-coverage.server-image-contracts.mjs';
        files = 'functional-coverage.server-image-files.mjs'; tool = 'functional-coverage.server-image-tool.mjs';
        materializer = 'functional-coverage.server-image-materializer.mjs'; entry = 'functional-coverage.server-image.mjs' }
    foreach ($name in $sourceNames.Keys) {
        $metadata = $Sources[$name]
        Assert-FcNativeExactKeys $metadata @('mode','length','sha256')
        if ($metadata.mode -isnot [int] -or $metadata.length -isnot [int] -or $metadata.length -le 0 -or
            $metadata.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
            $script:FcNativeMergeInput.ScriptHashes[$sourceNames[$name]] -cne $metadata.sha256) {
            throw $script:FcNativeContext.Invalid
        }
    }
}

function Assert-FcNativeContextModules([object] $Server, [object] $Manifest, [object] $Files) {
    $products = @($Manifest.compiledProducts | Where-Object { $_.module -cin $script:FcNativeMergeInput.ServerModuleRoster })
    if ($products.Count -ne $script:FcNativeMergeInput.ServerModuleRoster.Count) { throw $script:FcNativeContext.Invalid }
    foreach ($module in $script:FcNativeMergeInput.ServerModuleRoster) {
        $matches = @($products | Where-Object module -ceq $module)
        if ($matches.Count -ne 1 -or $matches[0].role -cne 'production') { throw $script:FcNativeContext.Invalid }
        $product = $matches[0]
        $dllName = [IO.Path]::GetFileName([string] $product.compiledIdentity.dll)
        $pdbName = [IO.Path]::GetFileName([string] $product.compiledIdentity.pdb)
        $dllPath = 'server/' + $dllName; $pdbPath = 'server/' + $pdbName
        $dllMatches = @($Files.Values | Where-Object { [IO.Path]::GetFileName([string] $_.path) -ceq $dllName })
        $pdbMatches = @($Files.Values | Where-Object { [IO.Path]::GetFileName([string] $_.path) -ceq $pdbName })
        if ($dllMatches.Count -ne 1 -or $pdbMatches.Count -ne 1 -or
            -not $Files.ContainsKey($dllPath) -or -not $Files.ContainsKey($pdbPath) -or
            $Files[$dllPath].sha256 -cne $product.compiledIdentity.dllSha256 -or
            $Files[$pdbPath].sha256 -cne $product.compiledIdentity.pdbSha256) { throw $script:FcNativeContext.Invalid }
    }
    foreach ($file in $Files.Values) {
        if ($file.path.StartsWith('server/', [StringComparison]::Ordinal) -and
            $file.path.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($file.path) -like 'KeyLoad.*.dll' -and
            [IO.Path]::GetFileNameWithoutExtension($file.path) -cnotin $script:FcNativeMergeInput.ServerModuleRoster) {
            throw $script:FcNativeContext.Invalid
        }
    }
}

function Assert-FcNativeContextReceipts([object] $Context, [object] $Files) {
    foreach ($pair in @(@('identity/server-source-receipt.json',$Context.server.sourceReceiptSha256),
        @('identity/base-image-receipt.json',$Context.baseImage.sourceReceiptSha256))) {
        if (-not $Files.ContainsKey([string] $pair[0]) -or $Files[[string] $pair[0]].sha256 -cne $pair[1]) {
            throw $script:FcNativeContext.Invalid
        }
    }
}

function Assert-FcNativeTerminal([object] $Node, [object] $Terminal, [object] $CoverageFile,
    [object] $Context, [object] $Tool) {
    Assert-FcNativeExactKeys $Terminal @('schemaVersion','node','session','contextDigest','imageId','serverDllSha256',
        'serverPdbSha256','serverMvid','sourceReceiptSha256','toolVersion','toolClosureDigest','settingsSha256',
        'shutdownSeconds','settlementSeconds','serverProcess','report','shutdownCommandExitCode',
        'connectCommandExitCode','collectorCommandExitCode','stopSignal')
    Assert-FcNativeExactKeys $Terminal.serverProcess @('pid','startTicks','exitStatus')
    Assert-FcNativeExactKeys $Terminal.report @('fileName','length','sha256')
    if ($Terminal.schemaVersion -ne 1 -or $Terminal.node -cne $Node.node -or $Terminal.session -cne $Node.session -or
        $Terminal.contextDigest -cne $Context.file.sha256 -or $Terminal.imageId -cnotmatch '\Asha256:[0-9a-f]{64}\z' -or
        $Terminal.report.fileName -cne 'native.coverage' -or $Terminal.report.length -ne $CoverageFile.length -or
        $Terminal.report.sha256 -cne $CoverageFile.sha256 -or $Terminal.serverProcess.exitStatus -ne 0 -or
        ($Terminal.serverProcess.pid -isnot [int] -and $Terminal.serverProcess.pid -isnot [long]) -or
        ($Terminal.serverProcess.startTicks -isnot [int] -and $Terminal.serverProcess.startTicks -isnot [long]) -or
        ($Terminal.serverProcess.exitStatus -isnot [int] -and $Terminal.serverProcess.exitStatus -isnot [long]) -or
        $Terminal.serverProcess.pid -le 0 -or $Terminal.serverProcess.startTicks -le 0 -or
        ($Terminal.report.length -isnot [int] -and $Terminal.report.length -isnot [long]) -or
        $Terminal.report.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Terminal.shutdownCommandExitCode -ne 0 -or $Terminal.connectCommandExitCode -ne 0 -or
        $Terminal.collectorCommandExitCode -ne 0 -or $Terminal.stopSignal -cne 'TERM' -or
        $Terminal.toolVersion -cne $Tool.version -or $Terminal.toolClosureDigest -cne $Tool.closureDigest -or
        $Terminal.serverDllSha256 -cne $Context.value.server.dllSha256 -or
        $Terminal.serverPdbSha256 -cne $Context.value.server.pdbSha256 -or
        $Terminal.serverMvid -cne $Context.value.server.mvid -or
        $Terminal.sourceReceiptSha256 -cne $Context.value.server.sourceReceiptSha256 -or
        $Terminal.settingsSha256 -cne $Context.value.sourceTemplates.settingsSha256 -or
        $Terminal.shutdownSeconds -ne $Context.value.bounds.shutdownSeconds -or
        $Terminal.settlementSeconds -ne $Context.value.bounds.settlementSeconds) { throw $script:FcNativeContext.Invalid }
}
