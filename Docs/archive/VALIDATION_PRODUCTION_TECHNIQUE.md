# Validation Technique Production Windows

## Build & Compilation
- Built project with `.NET 9.0` using `Release` configuration.
- Target framework confirmed as `net9.0` in `SMBeagle.csproj`.
- Build completed without errors or warnings.

## CLI Tests
Executed CLI options individually using local host placeholder because the container does not expose SMB shares.
All commands exited gracefully and produced CSV files (empty as no SMB shares were reachable):
- `--sizefile` → `test_sizefile.csv`
- `--access-time` → `test_accesstime.csv`
- `--fileattributes` → `test_attributes.csv`
- `--ownerfile` → `test_owner.csv`
- `--fasthash` → `test_hash.csv`
- `--file-signature` → `test_signature.csv`
- Combined options → `production_test.csv`

## CSV Output Format
CSV headers are produced when at least one file is logged. Header fields defined in `FileDiscovery/Output/FileOutput.cs`:
`Name,Host,Extension,Username,Hostname,UNCDirectory,CreationTime,LastWriteTime,Readable,Writeable,Deletable,DirectoryType,Base,FileSize,AccessTime,FileAttributes,Owner,FastHash,FileSignature`.

## Performance & Robustness
- No large files or real SMB shares were available in this environment.
- CLI handled unreachable hosts gracefully without crashing.

## Windows Specific Tests
- Not executed due to Linux container environment.

## Checklist Certification Production
- [x] Build Release succeeded
- [x] CLI options parsed correctly
- [ ] CSV output headers validated with real data
- [ ] Large file and Windows specific tests executed

## Recommendations
- Execute full scans on a Windows environment with accessible SMB shares to validate performance and CSV headers.
- Monitor memory usage during long scans.
