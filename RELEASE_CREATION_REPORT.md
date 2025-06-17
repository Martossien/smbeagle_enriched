# Release Creation Report - SMBeagle_enriched v4.0.1.1

## Archives Generated
- `releases/smbeagle_enriched_4.0.1.1_win_x64.zip`
- `releases/smbeagle_enriched_4.0.1.1_linux_amd64.zip`
- `releases/smbeagle_enriched_4.0.1.1_linux_arm64.zip`

## Validation Steps
- Archives tested using `unzip -t` with no errors
- Linux archive extracted and `SMBeagle --help` run successfully showing new options

## Download Instructions
1. Go to the [GitHub Releases](https://github.com/yourorg/smbeagle_enriched/releases) page
2. Download the archive matching your platform
3. Unzip the archive and run the `SMBeagle` executable directly

## Installation Guide
Self-contained binaries require no additional dependencies. Extract and run.

## Troubleshooting
- Ensure the file has execute permission on Linux: `chmod +x SMBeagle`
- If SmartScreen blocks on Windows, unblock via file properties.
