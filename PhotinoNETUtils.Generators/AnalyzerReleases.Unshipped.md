; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

| Rule ID | Category                     | Severity | Notes                                              |
|---------|------------------------------|----------|----------------------------------------------------|
| PNU1001 | PhotinoNetUtils.Settings     | Error    | Settings model cannot be nested or generic         |
| PNU1002 | PhotinoNetUtils.Settings     | Error    | Duplicate setting key                              |
| PNU1003 | PhotinoNetUtils.Settings     | Warning  | Settings model cannot be normalized                |
| PNU1101 | PhotinoNetUtils.Messaging    | Error    | Native message class must be partial               |
| PNU1102 | PhotinoNetUtils.Messaging    | Error    | Native message class is not supported              |
| PNU1103 | PhotinoNetUtils.Messaging    | Error    | Native message method has an unsupported signature |
| PNU1104 | PhotinoNetUtils.Messaging    | Error    | Duplicate native message type                      |
| PNU1105 | PhotinoNetUtils.Messaging    | Warning  | Native message type is handled twice               |
| PNU1201 | PhotinoNetUtils.Settings     | Warning  | Settings payload is missing a setting              |
| PNU2001 | PhotinoNetUtils.Messaging    | Warning  | Native payload is not registered                   |
| PNU2101 | PhotinoNetUtils.Localization | Warning  | Unknown localization key                           |
| PNU2102 | PhotinoNetUtils.Localization | Error    | Duplicate localization key                         |
| PNU2103 | PhotinoNetUtils.Localization | Error    | Localization key does not follow the format        |
| PNU2104 | PhotinoNetUtils.Localization | Warning  | Localization key is missing from a locale          |
| PNU3001 | PhotinoNetUtils.Utils        | Error    | Singleton class must be partial                    |
| PNU3002 | PhotinoNetUtils.Utils        | Error    | Singleton class is not supported                   |
| PNU3003 | PhotinoNetUtils.Utils        | Error    | Singleton needs a parameterless constructor        |
| PNU3004 | PhotinoNetUtils.Utils        | Error    | Singleton class already declares Instance          |
| PNU3005 | PhotinoNetUtils.Utils        | Error    | Singleton constructor must be private              |
| PNU3006 | PhotinoNetUtils.Utils        | Warning  | Singleton class is sealed by the generator         |
