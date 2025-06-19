# SMBeagle_enriched

[![Maintenance](https://img.shields.io/badge/Maintained%3F-yes-green.svg)](https://github.com/Martossien/smbeagle_enriched)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-lightgrey.svg)](https://github.com/Martossien/smbeagle_enriched/releases)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

SMBeagle_enriched is an enhanced fork of the excellent [SMBeagle](https://github.com/punk-security/smbeagle) project by [Punk Security](https://www.punksecurity.co.uk). This version extends the original tool with additional file metadata collection capabilities and local filesystem scanning while preserving all existing functionality.

## Acknowledgments

This project builds upon the outstanding work of the SMBeagle team at Punk Security. All core SMB enumeration, permission checking, and output functionality comes from their original implementation. We extend our sincere gratitude for creating such a solid foundation for SMB file share auditing.

**Original SMBeagle**: https://github.com/punk-security/smbeagle

## What's New

SMBeagle_enriched adds **seven enhanced capabilities** to the original tool:

### **Enhanced Metadata Collection (6 options)**
- `--sizefile` - Collect file sizes in bytes
- `--access-time` - Collect last access timestamps  
- `--fileattributes` - Collect Windows file system attributes
- `--ownerfile` - Collect file ownership information
- `--fasthash` - Generate fast xxHash64 checksums
- `--file-signature` - Detect file types via magic byte analysis

### **Local Filesystem Scanning (NEW)**
- `--local-path` - Scan local directories instead of SMB network discovery

These additions support forensic analysis, data governance, security auditing, and offline system analysis workflows.

## Platform Compatibility

### Windows Support
SMBeagle_enriched provides full functionality on Windows platforms:

✅ **Fully Supported:**
- All original SMBeagle features
- Native Windows authentication (no credentials required)
- All seven new capabilities with Win32 API integration
- File ownership resolution (`--ownerfile`) with domain integration
- Windows-specific file attributes (`--fileattributes`)
- Local filesystem scanning (`--local-path`)

### Linux Support  
When running on Linux or using explicit credentials:

✅ **Supported:**
- All original SMBeagle cross-platform features
- SMBLibrary-based remote enumeration
- File size, access time, and basic attributes collection
- Hash calculation and file signature detection
- Local filesystem scanning with Unix permissions

⚠️ **Limitations:**
- `--ownerfile` returns `<NOT_SUPPORTED>` for SMB scans (Windows-only feature)
- File attributes may have different representations
- Requires explicit username/password authentication for SMB

## Installation

### Requirements
- .NET 9.0 Runtime or SDK
- Windows 10+ or Linux (glibc 2.17+)
- Network access to target SMB shares (for network scanning)

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

### Network SMB Scanning (Original Functionality)

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

### Local Filesystem Scanning (NEW)

**Basic local directory scan:**
```bash
# Single directory
SMBeagle --local-path /home/data -c local_scan.csv

# Multiple directories  
SMBeagle --local-path /var/log /opt/data /home -c multi_scan.csv
```

**Complete local audit with all metadata:**
```bash
# Windows
SMBeagle.exe --local-path C:\Users --sizefile --access-time --fileattributes --ownerfile --fasthash --file-signature -c complete_audit.csv -v

# Linux
./SMBeagle --local-path /home /var /opt --sizefile --access-time --fasthash --file-signature -c linux_audit.csv -v
```

**Offline forensic analysis:**
```bash
# Mounted evidence drive
SMBeagle --local-path /mnt/evidence --sizefile --access-time --ownerfile --fasthash --file-signature -c evidence_analysis.csv
```

### Enhanced Command Line Options

| Option | Description | Platform Support |
|--------|-------------|-------------------|
| `--local-path` | Scan local directories (multiple accepted) | Windows, Linux |
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

## Use Cases

### **Network Security Auditing**
```bash
# Discover sensitive files on network shares
SMBeagle -e elasticsearch:9200 -n 10.0.0.0/8 --file-signature --fasthash --ownerfile
```

### **Local System Forensics**
```bash
# Analyze local system for incident response
SMBeagle --local-path /home /var/log --sizefile --access-time --file-signature -c forensic.csv
```

### **Data Governance & Compliance**
```bash
# Complete data inventory with ownership
SMBeagle -c inventory.csv --local-path /data --sizefile --ownerfile --fileattributes --access-time
```

### **Offline Analysis**
```bash
# Scan mounted drives without network access
SMBeagle --local-path /mnt/backup --fasthash --file-signature -c backup_analysis.csv
```

## Technical Implementation

### Performance Considerations
- Metadata collection adds minimal overhead to existing enumeration
- Hash calculation limited to first 64KB for performance
- File signature detection reads only magic byte headers (32 bytes)
- Owner resolution uses SID caching to reduce Windows API calls
- Local scanning bypasses network latency for faster processing

### Dependencies
- **FileSignatures 5.2.0** - Magic byte file type detection
- **System.IO.Hashing 9.0.0** - Native .NET 9 xxHash64 implementation  
- **Mono.Posix.NETStandard 5.20.1** - Unix permissions support
- Original SMBeagle dependencies preserved

## Current Status

🚀 **Production Ready**: This enhanced version has been tested across various Windows and Linux environments and is ready for production use.

## Limitations and Known Issues

- **Cross-platform ownership**: `--ownerfile` only functions on Windows platforms for SMB scans
- **Local vs Network**: `--local-path` is mutually exclusive with network discovery options
- **Large file performance**: Hash calculation on very large files may impact enumeration speed
- **File signature accuracy**: Detection relies on magic bytes and may not identify all file types correctly

## Contributing

This project maintains compatibility with the original SMBeagle architecture. When contributing:
- Preserve backward compatibility with existing functionality
- Follow the established pattern for metadata collection features
- Test on both Windows and Linux platforms
- Document platform-specific limitations clearly

## Support and Issues

For issues specific to the enhanced metadata features or local scanning, please use this repository's issue tracker. For core SMBeagle functionality, consider reporting to the [original project](https://github.com/punk-security/smbeagle/issues) first.

## License

This project maintains the same Apache License 2.0 as the original SMBeagle project. See [LICENSE](LICENSE) for details.

## Thanks

Special thanks to the Punk Security team for creating SMBeagle and making it available to the security community. This enhanced version aims to extend their excellent work while maintaining the same quality and reliability standards.
