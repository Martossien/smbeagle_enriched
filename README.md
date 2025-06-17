# SMBeagle_enriched

[![Maintenance](https://img.shields.io/badge/Maintained%3F-yes-green.svg)](https://github.com/Martossien/smbeagle_enriched)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-lightgrey.svg)](https://github.com/Martossien/smbeagle_enriched/releases)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

SMBeagle_enriched is an enhanced fork of the excellent [SMBeagle](https://github.com/punk-security/smbeagle) project by [Punk Security](https://www.punksecurity.co.uk). This version extends the original tool with additional file metadata collection capabilities while preserving all existing functionality.

## Acknowledgments

This project builds upon the outstanding work of the SMBeagle team at Punk Security. All core SMB enumeration, permission checking, and output functionality comes from their original implementation. We extend our sincere gratitude for creating such a solid foundation for SMB file share auditing.

**Original SMBeagle**: https://github.com/punk-security/smbeagle

## What's New

SMBeagle_enriched adds six additional metadata collection options to the original tool:

- `--sizefile` - Collect file sizes in bytes
- `--access-time` - Collect last access timestamps  
- `--fileattributes` - Collect Windows file system attributes
- `--ownerfile` - Collect file ownership information
- `--fasthash` - Generate fast xxHash64 checksums
- `--file-signature` - Detect file types via magic byte analysis

These additions are designed to support forensic analysis, data governance, and enhanced security auditing workflows.

## Platform Compatibility

### Windows Support
SMBeagle_enriched provides full functionality on Windows platforms:

✅ **Fully Supported:**
- All original SMBeagle features
- Native Windows authentication (no credentials required)
- All six new metadata options with Win32 API integration
- File ownership resolution (`--ownerfile`) with domain integration
- Windows-specific file attributes (`--fileattributes`)

### Linux Support  
When running on Linux or using explicit credentials:

✅ **Supported:**
- All original SMBeagle cross-platform features
- SMBLibrary-based remote enumeration
- File size, access time, and basic attributes collection
- Hash calculation and file signature detection

⚠️ **Limitations:**
- `--ownerfile` returns `<NOT_SUPPORTED>` (Windows-only feature)
- File attributes may have different representations
- Requires explicit username/password authentication

### Cross-Platform Technical Notes

The tool automatically detects the runtime platform and selects appropriate implementation:
- **Windows**: Uses native Win32 APIs for maximum performance and feature completeness
- **Linux**: Uses SMBLibrary for network-based SMB operations
- **Authentication**: Windows supports integrated auth; Linux requires explicit credentials

## Installation

### Requirements
- .NET 9.0 Runtime or SDK
- Windows 10+ or Linux (glibc 2.17+)
- Network access to target SMB shares

### Binary Installation
Download the appropriate pre-compiled binary from [Releases](https://github.com/Martossien/smbeagle_enriched/releases):

**Windows:**
```cmd
# Download and extract smbeagle_enriched_4.0.1.1_win_x64.zip
SMBeagle.exe --help
```

**Linux:**
```bash
# Download and extract for your architecture
wget https://github.com/Martossien/smbeagle_enriched/releases/download/v4.0.1.1/smbeagle_enriched_4.0.1.1_linux_amd64.zip
unzip smbeagle_enriched_4.0.1.1_linux_amd64.zip
chmod +x SMBeagle
./SMBeagle --help
```

### Building from Source
```bash
git clone https://github.com/Martossien/smbeagle_enriched.git
cd smbeagle_enriched
dotnet restore
dotnet build --configuration Release
```

## Usage

### Basic Examples

**Standard SMB enumeration with enhanced metadata:**
```bash
# Windows (integrated auth)
SMBeagle.exe -c results.csv --sizefile --access-time

# Linux (explicit credentials)  
./SMBeagle -c results.csv -u username -p password --sizefile --access-time
```

**Forensic analysis with all metadata:**
```bash
SMBeagle -c forensic_audit.csv --sizefile --access-time --fileattributes --ownerfile --fasthash --file-signature -v
```

**Network discovery with metadata collection:**
```bash
SMBeagle -e elasticsearch:9200 -n 192.168.1.0/24 --fasthash --file-signature
```

### New Command Line Options

| Option | Description | Platform Support |
|--------|-------------|-------------------|
| `--sizefile` | Collect file sizes in bytes | Windows, Linux |
| `--access-time` | Collect last access timestamps | Windows, Linux |
| `--fileattributes` | Collect file system attributes | Windows (full), Linux (basic) |
| `--ownerfile` | Collect file ownership (DOMAIN\\Username) | Windows only |
| `--fasthash` | Generate xxHash64 checksums (first 64KB) | Windows, Linux |
| `--file-signature` | Detect file types via magic bytes | Windows, Linux |

### CSV Output Schema

The enhanced version adds these columns to the standard SMBeagle CSV output:
- `FileSize` - File size in bytes (0 if not collected)
- `AccessTime` - Last access timestamp
- `FileAttributes` - File system attributes string
- `Owner` - File owner (DOMAIN\\Username or `<NOT_SUPPORTED>`)
- `FastHash` - xxHash64 checksum (hex string)
- `FileSignature` - Detected file type (e.g., "pdf", "docx", "unknown")

## Technical Implementation

### Performance Considerations
- Metadata collection adds minimal overhead to existing enumeration
- Hash calculation limited to first 64KB for performance
- File signature detection reads only magic byte headers (32 bytes)
- Owner resolution uses SID caching to reduce Windows API calls

### Dependencies
- **FileSignatures 5.2.0** - Magic byte file type detection
- **System.IO.Hashing 9.0.0** - Native .NET 9 xxHash64 implementation  
- **K4os.Hash.xxHash 1.0.8** - Alternative xxHash implementation
- Original SMBeagle dependencies preserved

### Architecture Notes
The enhanced metadata collection follows a consistent pattern:
1. CLI option parsing and propagation through `FileFinder`
2. Conditional metadata collection in `Directory` enumeration methods
3. Platform-specific implementation in `WindowsHelper`/`CrossPlatformHelper`
4. Unified output through existing `FileOutput` schema

## Current Status

🚧 **Testing Phase**: This enhanced version is currently undergoing comprehensive testing across various Windows and Linux environments. While the core functionality is stable, users should validate behavior in their specific environments.

## Docker Support

Docker support is not included in this release. Users requiring containerized deployment should use the original SMBeagle project or deploy using the native binaries with appropriate volume mounts.

## Limitations and Known Issues

- **Cross-platform ownership**: `--ownerfile` only functions on Windows platforms
- **Large file performance**: Hash calculation on very large files may impact enumeration speed
- **Memory usage**: Extensive metadata collection on large shares may increase memory consumption
- **File signature accuracy**: Detection relies on magic bytes and may not identify all file types correctly

## Contributing

This project maintains compatibility with the original SMBeagle architecture. When contributing:
- Preserve backward compatibility with existing functionality
- Follow the established pattern for metadata collection features
- Test on both Windows and Linux platforms
- Document platform-specific limitations clearly

## Support and Issues

For issues specific to the enhanced metadata features, please use this repository's issue tracker. For core SMBeagle functionality, consider reporting to the [original project](https://github.com/punk-security/smbeagle/issues) first.

## License

This project maintains the same Apache License 2.0 as the original SMBeagle project. See [LICENSE](LICENSE) for details.

## Thanks

Special thanks to the Punk Security team for creating SMBeagle and making it available to the security community. This enhanced version aims to extend their excellent work while maintaining the same quality and reliability standards.