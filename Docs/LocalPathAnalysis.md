# Analysis for Implementing --local-path Functionality in SMBeagle_enriched

This document outlines the necessary modifications to implement the `--local-path` feature in the SMBeagle_enriched project. This feature will allow users to scan local directories directly, bypassing the network and share discovery mechanisms.

## 1. Overview of Changes

The implementation will primarily involve modifications in `Program.cs` to handle the new command-line option and in `FileDiscovery/FileFinder.cs` to process local paths. The existing metadata extraction capabilities are expected to be compatible with local files.

## 2. Modifications in `Program.cs`

### 2.1. Reading the `LocalPaths` Option

The `Options` class in `Program.cs` already defines the `LocalPaths` property:

```csharp
[Option("local-path", HelpText = "Scan specific local directory path (e.g., C:\Users\martos)")]
public IEnumerable<string> LocalPaths { get; set; }
```

This option will be parsed by the `CommandLineParser` library. We need to check if `opts.LocalPaths` has any values within the `Run` method.

### 2.2. Conditional Logic for Local Path Scanning

A primary change in `Program.cs` will be to introduce conditional logic based on whether `opts.LocalPaths` is provided and contains valid paths.

If `opts.LocalPaths` is populated:
- **Skip Network and Host Discovery:** Steps 1, 2, 3, 4, and 5 (NetworkFinder, HostFinder, ShareDiscovery) should be bypassed. Appropriate messages should be logged to indicate that a local scan is being performed.
- **Prepare for `FileFinder`:** Instead of collecting `Share` objects from network scans, we will need to prepare a different input for `FileFinder` or adapt `FileFinder` to accept local paths.

Example conditional structure in `Run(Options opts)`:

```csharp
if (opts.LocalPaths != null && opts.LocalPaths.Any())
{
    OutputHelper.WriteLine("Performing local path scan as --local-path is specified...");
    // Proceed with local path scanning logic (details in FileFinder section)
    // Instantiate FileFinder with local paths

    // Example (conceptual):
    // var fileFinder = new FileFinder(localPaths: opts.LocalPaths.ToList(), ... other options ...);
}
else
{
    // Existing network scanning logic
    OutputHelper.WriteLine("1. Performing network discovery...");
    // ... nf.DiscoverNetworks(); ...
    // ... HostFinder hf = new(...); ...
    // ... Share discovery ...
    // ... FileFinder ff = new(shares: shares, ...); ...
}
```

### 2.3. Instantiating `FileFinder` for Local Paths

When `opts.LocalPaths` is used, the `FileFinder` instantiation will need to change. The current constructor `public FileFinder(List<Share> shares, ...)` expects a list of `Share` objects.

Two main approaches can be considered:

1.  **Modify `FileFinder` Constructor:** Overload or modify the `FileFinder` constructor to accept a list of local paths. This is a cleaner approach.
    ```csharp
    // In FileFinder.cs - New or modified constructor
    // public FileFinder(List<string> localPaths, string outputDirectory, ...)
    // { ... initialization for local paths ... }
    ```
2.  **Create Dummy `Share` Objects:** If modifying `FileFinder` extensively is to be avoided for this analysis phase, create "dummy" `Share` and `Host` objects for each local path. This aligns with the approach hinted at in the commented-out `GetLocalDriveDirectories` and the user's proposed `GetLocalPathDirectories`.

    ```csharp
    // In Program.cs, if using dummy Share objects:
    List<Share> localShares = new List<Share>();
    var dummyHost = new Host("localhost"); // Or some other local identifier
    foreach (string path in opts.LocalPaths)
    {
        if (System.IO.Directory.Exists(path)) // Basic validation
        {
            // The share name could be the path itself or a derived name
            var dummyShare = new Share(dummyHost, $"LOCAL_{System.IO.Path.GetFileName(path)}", Enums.ShareTypeEnum.DISK);
            // We might need a way to pass the actual full path to the Directory object
            // This part needs careful mapping to how Directory objects are constructed in FileFinder
            // For instance, the Directory constructor might need to understand that its 'path' argument
            // is a full local path rather than a path relative to a share.
            localShares.Add(dummyShare);
            // This dummy share would then be used by FileFinder, which would need minor adjustments
            // to use the provided path as a local root.
        }
        else
        {
            OutputHelper.WriteLine($"ERROR: Local path not found or inaccessible: {path}");
        }
    }
    // Then instantiate FileFinder:
    // FileFinder ff = new FileFinder(shares: localShares, ... flags for local scan ...);
    ```
    The analysis leans towards modifying `FileFinder` (Approach 1) for a cleaner design, but creating dummy objects (Approach 2) is a fallback if minimal changes to `FileFinder` are strictly required. The user's provided snippet for `GetLocalPathDirectories` already uses a dummy `Share` and `Host`.

The chosen approach will directly influence the changes needed in `FileFinder.cs`. The key is to ensure `FileFinder` receives the local paths and can process them.

## 3. Modifications in `FileDiscovery/FileFinder.cs`

### 3.1. Implementing `GetLocalPathDirectories`

As suggested in the issue, a method to convert local string paths into `Directory` objects is needed. This method will create a "dummy" `Share` and `Host` to fit the existing structure, which is a pragmatic approach for integrating local paths with minimal disruption.

```csharp
// To be added in FileDiscovery/FileFinder.cs
private List<Directory> GetLocalPathDirectories(List<string> localPaths)
{
    var directories = new List<Directory>();
    // Using "localhost" is suitable as these are local paths.
    var dummyHost = new HostDiscovery.Host("localhost");
    // Share name "LOCAL" signifies it's not a real network share.
    var dummyShare = new ShareDiscovery.Share(dummyHost, "LOCAL_SCAN", Enums.ShareTypeEnum.DISK);

    foreach (string path in localPaths)
    {
        if (System.IO.Directory.Exists(path))
        {
            // The Directory constructor takes the path relative to the share.
            // For local paths, the 'path' argument to the Directory constructor should be the full path.
            // However, the current Directory constructor is `Directory(string path, Share share)`.
            // We need to ensure UNCPath or similar properties are correctly formed for local context.
            // Let's assume Directory class can be instantiated or adapted slightly.
            // A simple approach is to treat the 'path' argument of Directory constructor as the full local path
            // when the Share is of a special local type or has a specific name.

            var directoryInstance = new Directory(path: path, share: dummyShare)
            {
                // It's crucial that DirectoryTypeEnum.LOCAL_FIXED (or similar) is used.
                // This might require Directory.UNCPath or other path properties to handle it correctly.
                // For example, Directory.UNCPath might simply return the 'path' if it's a local directory.
                DirectoryType = Enums.DirectoryTypeEnum.LOCAL_FIXED
            };
            directories.Add(directoryInstance);
            OutputHelper.WriteLine($"Added local directory for scanning: {path}", 1);
        }
        else
        {
            OutputHelper.WriteLine($"WARNING: Local path '{path}' does not exist or is not accessible. Skipping.", 1);
        }
    }
    return directories;
}
```

### 3.2. Integrating Local Path Scanning into `FileFinder`

There are a few ways to integrate this:

**Option A: Modify existing constructor (less ideal if keeping network scanning intact is priority for this analysis)**

The existing constructor `public FileFinder(List<Share> shares, ...)` could be modified to check the type of shares or have an additional parameter.

**Option B: New constructor for local paths (cleaner, as suggested in Program.cs analysis)**

```csharp
// New or overloaded constructor in FileDiscovery/FileFinder.cs
public FileFinder(List<string> localPaths, string outputDirectory, bool fetchFiles, List<String> filePatterns, bool getPermissionsForSingleFileInDir = true, bool enumerateAcls = true, bool quiet = false, bool verbose = false, /* no crossPlatform needed for local? */ bool includeFileSize = false, bool includeAccessTime = false, bool includeFileAttributes = false, bool includeFileOwner = false, bool includeFastHash = false, bool includeFileSignature = false)
{
    // Initialize common properties like metadata flags
    _includeFileSize = includeFileSize;
    _includeAccessTime = includeAccessTime;
    // ... and so on for other metadata flags and options ...
    pClientContext = IntPtr.Zero; // No client context for local files in the same way as SMB

    // Convert local string paths to Directory objects
    _directories = GetLocalPathDirectories(localPaths);

    if (!_directories.Any())
    {
        OutputHelper.WriteLine("No valid local directories to scan. Exiting.", 1);
        return; // Or handle appropriately
    }

    if (!quiet)
        OutputHelper.WriteLine($"Enumerating files and subdirectories for local paths...");

    // The rest of the logic from the original constructor for directory splitting,
    // file enumeration, and metadata fetching needs to be adapted.
    // Crucially, the calls to `dir.FindDirectoriesRecursively` and `dir.FindFilesRecursively`
    // need to work correctly for local paths. The `crossPlatform` flag might be irrelevant or always true/false for local.
    // For Windows, `System.IO` calls will be used. For Linux/macOS, similar local file system APIs.
    // The `crossPlatform` parameter to these methods might need to be set to `true` or handled specifically for local scans.

    // Simplified loop structure for local paths:
    Console.CancelKeyPress += handler; // Assuming 'handler' is defined as in the original constructor
    foreach (Directory dir in _directories) // These are now top-level local paths
    {
        OutputHelper.WriteLine($"Enumerating all subdirectories for '{dir.Path}' - CTRL-BREAK to SKIP", 1, false);
        // Assuming dir.Path for local directories holds the full path.
        // The 'crossPlatform' flag's relevance for local paths:
        // System.IO operations are generally cross-platform in .NET.
        // However, if `FindDirectoriesRecursively` or `FindFilesRecursively` have OS-specific implementations triggered by `crossPlatform`,
        // this needs to be set appropriately (likely `true` or a new dedicated mode).
        // For local files, many SMB-specific parts (like `pClientContext` for WindowsHelper) would be bypassed.
        bool abort = false; // Define abort flag
        dir.FindDirectoriesRecursively(crossPlatform: true, ref abort); // Assuming true for local, or adapt method
        // ... (SplitLargeDirectories might still be useful) ...
        OutputHelper.WriteLine($"Enumerating files in '{dir.Path}' - CTRL-BREAK to SKIP", 1, false);
        dir.FindFilesRecursively(crossPlatform: true, ref abort, extensionsToIgnore: new List<string>() { ".dll",".manifest",".cat" },
                                includeFileSize: _includeFileSize, includeAccessTime: _includeAccessTime,
                                includeFileAttributes: _includeFileAttributes, includeFileOwner: _includeFileOwner,
                                includeFastHash: _includeFastHash, includeFileSignature: _includeFileSignature, verbose: verbose);

        foreach (File file in dir.RecursiveFiles)
        {
            // Path construction for `FilesSentForOutput` needs to be consistent.
            // For local files, it could be just the full path.
            string fileKey = file.FullName.ToLower(); // Assuming File.FullName is the full local path
            if (FilesSentForOutput.Add(fileKey))
            {
                if (enumerateAcls) // ACL fetching will use local APIs
                    FetchFilePermissionLocal(file); // A new or adapted method for local ACLs

                OutputHelper.AddPayload(new Output.FileOutput(file), Enums.OutputtersEnum.File);

                // File fetching (-g option) for local files means copying them to the loot directory.
                // This part should work fine if paths are correctly handled.
                if (fetchFiles && filePatterns.Any(pattern => Regex.IsMatch(file.Name, pattern, RegexOptions.IgnoreCase)))
                {
                    // tasks.Add(Task.Run(() => FetchLocalFile(file, outputDirectory)));
                    // For local files, async copying might still be beneficial but simpler.
                    FetchLocalFile(file, outputDirectory); // Simplified for analysis
                }
            }
        }
        dir.Clear();
        // CacheACL.Clear(); // ACL Caching logic might need review for local context
    }
    // Task.WaitAll(tasks.ToArray());
    Console.CancelKeyPress -= handler; // Assuming 'handler' is defined
    OutputHelper.WriteLine($"Local file enumeration complete, {FilesSentForOutput.Count} files identified.");
}

// New method for local ACLs
private void FetchFilePermissionLocal(File file)
{
    // Utilize System.IO.File.GetAccessControl() or platform-specific APIs.
    // This will be different from SMB permission fetching.
    // The ACL object structure might need to be populated differently.
    // For simplicity of analysis, we assume ACLs can be fetched.
    // Example:
    try
    {
        var fileInfo = new System.IO.FileInfo(file.FullName);
        // var fileSecurity = fileInfo.GetAccessControl(); // Windows specific
        // On Linux/macOS, POSIX permissions would be fetched.
        // This part requires careful implementation to populate the existing ACL model.
        // For this analysis, we acknowledge it's different but feasible.
        // file.SetPermissionsFromACL( ... converted ACL ...);
        OutputHelper.WriteLine($"Fetched permissions for local file: {file.FullName}", 2); // Verbose
    }
    catch (Exception ex)
    {
        OutputHelper.WriteLine($"WARNING: Could not fetch permissions for local file {file.FullName}: {ex.Message}", 2);
    }
}

// New method for "fetching" (copying) local files
private void FetchLocalFile(File file, string outputDirectory)
{
    try
    {
        string destinationFileName = System.IO.Path.Combine(outputDirectory, System.IO.Path.GetFileName(file.FullName));
        // Add logic to prevent path traversal if outputDirectory or file.FullName are manipulated
        // and to handle duplicate filenames if necessary.
        System.IO.File.Copy(file.FullName, destinationFileName, overwrite: true); // Or handle overwrite as an option
        OutputHelper.WriteLine($"Copied local file {file.FullName} to {destinationFileName}", 1);
    }
    catch (Exception ex)
    {
        OutputHelper.WriteLine($"ERROR: Failed to copy local file {file.FullName} to {outputDirectory}: {ex.Message}", 1);
    }
}

```

**Option C: Adapt existing constructor using a flag or by checking Share properties**

If `Program.cs` creates dummy `Share` objects, the main `FileFinder` constructor would receive these.
It would then need to identify these "local" shares.

```csharp
// In the existing FileFinder constructor:
// ...
// foreach (Share share in shares)
// {
//     if (share.Name == "LOCAL_SCAN" && share.Host.Address == "localhost") // Or use share.Type
//     {
//         // This is a local path passed as a Share object.
//         // The 'share.RootPath' (or a new property on Share) should contain the actual local directory path.
//         // The Directory constructor would be new Directory(share.RootPath, share)
//         // And its DirectoryType set to LOCAL_FIXED.
//         _directories.Add(new Directory(path: share.Path, share: share) { DirectoryType = Enums.DirectoryTypeEnum.LOCAL_FIXED });
//     }
//     else
//     {
//         _directories.Add(new Directory(path: "", share:share) { DirectoryType = Enums.DirectoryTypeEnum.SMB });
//     }
// }
// ...
// Then, the loops for FindDirectoriesRecursively and FindFilesRecursively need to check DirectoryType.
// if (dir.DirectoryType == Enums.DirectoryTypeEnum.LOCAL_FIXED) {
//     dir.FindFilesRecursively(crossPlatform: true, ...); // Use local file system logic
// } else {
//     dir.FindFilesRecursively(crossPlatform: crossPlatform, ...); // Existing SMB logic
// }
```
This option (C) integrates tightly with the "dummy share" approach from `Program.cs` and might be closer to the user's initial hypothesis structure. It requires careful conditional logic within the recursive methods or new methods like `FindFilesRecursivelyLocal`.

### 3.3. Compatibility of Metadata Extraction

The issue states: "_FindFilesWindows() utilise FileInfo qui marche sur chemins locaux. Toutes les métadonnées (size, access-time, attributes, owner, hash, signature) sont compatibles_". This is a key advantage.

-   **`FileInfo`**: The `System.IO.FileInfo` class in .NET is used for retrieving properties of local files. If `FindFilesRecursively` (or a new local version) uses `System.IO.Directory.GetFiles()` and `System.IO.Directory.GetDirectories()`, then for each file found, a `System.IO.FileInfo` object can be obtained.
-   **Metadata**:
    -   **Size**: `FileInfo.Length`
    -   **Access Times**: `FileInfo.LastAccessTimeUtc`, `FileInfo.LastWriteTimeUtc`, `FileInfo.CreationTimeUtc`.
    -   **Attributes**: `FileInfo.Attributes`.
    -   **Owner**: This is more platform-dependent. On Windows, `FileSecurity.GetOwner()` can be used. On Linux/macOS, POSIX owner information would need to be retrieved. The existing `WindowsHelper.ResolvePermissions` and `CrossPlatformHelper.ResolvePermissions` might need adaptation or new counterparts for purely local files if they are currently SMB-focused. The `File.SetPermissionsFromACL` would be populated accordingly.
    -   **Fast Hash**: The hashing mechanism should work identically as it operates on file streams. `System.IO.File.OpenRead()` can be used for local files.
    -   **File Signature**: This also operates on file streams and will be compatible.

The main adaptation for metadata will be in how owner/ACL information is retrieved for local files, ensuring it populates the existing `File` object's properties correctly. The `FetchFilePermissionLocal` method sketched above addresses this.

## 4. Error Handling and Other Considerations

### 4.1. Error Handling for Invalid Local Paths

-   **Non-existent Paths**: If a path provided via `--local-path` does not exist or is not accessible, SMBeagle should report this clearly and skip that path. The `GetLocalPathDirectories` method includes a basic `System.IO.Directory.Exists(path)` check and a warning message. This should be maintained.
-   **Permissions Issues**: While enumerating files or fetching metadata/ACLs for local files, the application might encounter paths it doesn't have permission to access. Standard `try-catch` blocks around `System.IO` operations should be used to handle `UnauthorizedAccessException` and other relevant IO exceptions. These errors should be logged, and the tool should continue processing other files/directories where possible.
    ```csharp
    // Example within a file processing loop
    try
    {
        // Process file
    }
    catch (UnauthorizedAccessException ex)
    {
        OutputHelper.WriteLine($"WARNING: Access denied to '{filePath}': {ex.Message}", 1);
    }
    catch (System.IO.IOException ex)
    {
        OutputHelper.WriteLine($"WARNING: IO error accessing '{filePath}': {ex.Message}", 1);
    }
    ```

### 4.2. Output and Logging Consistency

-   **CSV/Elasticsearch Output**: The issue states a major advantage is that the output format remains identical. This is achievable because the `File` object and its associated metadata (like `FileOutput`) are central to the output process. As long as local file scanning populates these `File` objects correctly (including their paths for identification), the existing `OutputHelper` and its sinks (CSV, Elasticsearch) should function without significant changes.
    -   The `File.FullName` property will be critical. For local files, this should be the absolute local path. For SMB files, it's the UNC path. The output consumers must be able to distinguish or correctly interpret these. The `Share.uncPath` part of the key `$"{{dir.Share.uncPath}}{{file.FullName}}"` used in `FilesSentForOutput` will need careful handling for local paths (e.g., the dummy share's `uncPath` could be "local://" or empty, and `file.FullName` would be the full path).
-   **Logging**: Console output (`OutputHelper.WriteLine`) should clearly indicate when a local scan is active. Verbose logging should provide details about the local paths being processed, similar to how it currently details SMB shares.

### 4.3. Impact on Existing Command-Line Options

-   **Mutually Exclusive Options**: The `--local-path` option should ideally be mutually exclusive with options related to network scanning (e.g., `--network`, `--host`, `--scan-local-shares`, `--disable-network-discovery`). If `--local-path` is specified, these other options could be ignored with a warning, or the tool could exit with an error indicating conflicting options. The `CommandLineParser` library might offer ways to define such exclusions.
    ```csharp
    // In Program.cs - Run(Options opts)
    if (opts.LocalPaths.Any() && (opts.Networks.Any() || opts.Hosts.Any() || opts.ScanLocalShares))
    {
        OutputHelper.WriteLine("WARNING: --local-path is specified; network scanning options (--network, --host, --scan-local-shares) will be ignored.", 0);
        // Or, make it an error:
        // OutputHelper.WriteLine("ERROR: --local-path cannot be used with network scanning options.", 0);
        // Environment.Exit(1);
    }
    ```
-   **Metadata and Filtering Options**: Options like `--sizefile`, `--access-time`, `--fasthash`, `--file-pattern`, `-g/--grab-files`, `--loot` (output directory for grabbed files) should work consistently for local scans. The analysis for `FileDiscovery/FileFinder.cs` already assumes these flags will be passed through and respected.
-   **Authentication Options**: `--username`, `--password`, `--domain` are irrelevant for local path scanning and should be ignored (possibly with a warning) if specified alongside `--local-path`.
-   **`--crossPlatform` Flag**: The internal `crossPlatform` flag's meaning might need to be nuanced. For local scans, operations will use standard .NET `System.IO` APIs which are inherently cross-platform. If the existing `crossPlatform` flag in `FileFinder` or its methods is used to switch between Windows-specific SMB libraries and a cross-platform SMB library, its role in a purely local scan context is diminished. It might be implicitly `true` or handled by a dedicated local scanning path.

### 4.4. Performance Considerations

-   **Advantage**: As noted in the issue, local scanning will be significantly faster due to no network latency and direct file system access.
-   **Potential Bottlenecks**: Hashing and file signature analysis can still be I/O intensive, even on local files. The existing mechanisms should perform well.

### 4.5. Testing

-   Thorough testing will be required for various scenarios:
    -   Different operating systems (Windows, Linux).
    -   Paths with spaces or special characters.
    -   Very deep directory structures.
    -   Large numbers of files.
    -   Permissions issues.
    -   Interaction with output options (CSV, Elasticsearch).

## 5. Conclusion

The analysis indicates that implementing the `--local-path` functionality is feasible and aligns well with the existing architecture of SMBeagle_enriched. The primary advantages highlighted in the initial request, such as the reuse of metadata extraction logic and output formats, appear to be valid.

**Key Recommendations:**

1.  **`Program.cs` Modifications**: Implement conditional logic to bypass network/share discovery when `--local-path` is used. Adopt a clear strategy for instantiating `FileFinder` for local paths, with a preference for a new or overloaded constructor in `FileFinder`.
2.  **`FileDiscovery/FileFinder.cs` Modifications**:
    *   Implement the `GetLocalPathDirectories` method as proposed to convert string paths to `Directory` objects using dummy `Share`/`Host` entities for compatibility.
    *   Adapt or overload the `FileFinder` constructor and internal file/directory enumeration logic to handle local paths. This includes ensuring `System.IO` APIs are used for file operations and that local ACL/owner information is fetched correctly.
    *   The `crossPlatform` flag usage will need to be clarified for local scans; standard .NET APIs are generally cross-platform.
3.  **Path Handling**: Ensure robust handling of local paths, including clear error messages for non-existent or inaccessible paths.
4.  **CLI Options**: Clearly define the interaction of `--local-path` with existing network-related CLI options, likely making them mutually exclusive.

The implementation will leverage .NET's `System.IO` capabilities, which are well-suited for this task. The most complex part will be ensuring that the `Directory` and `File` objects are populated in a way that is perfectly consistent with how they are populated during SMB scans, especially concerning path representations (e.g., `FullName`, `UNCPath` properties) to ensure seamless operation of downstream processing like metadata extraction and output generation.

This modification will significantly enhance SMBeagle_enriched by providing a direct way to audit local file systems, complementing its network scanning capabilities.
