# NAPS.Agent

## v11 Update

+ Fixed visibiity when showing forms from browser
+ Fixed asynchronous operations
+ Hided restoring window when connected
+ Agent more cross-platform friendly, removed windows-only dependencies
+ Added support for batch configuration from browser, use `json` format in `message` field
+ New api for batch \[`message`\]:
```json
{
    // All fields are optional
    "scans": 3,             // number of scans (repeats?), default: 1
    "interval": 1           // interval between scans in seconds, default: 1
    // ----------- Use one of the following --------
    "profileName": "def",   // profile name, default: null
    // --------------------- OR --------------------
    "duplex": true,         // is pages duplex (scanning mode), default: false
    "flip": true,           // flip duplex pages?, default: false
}
```

## Usage

> Note: it os better to add it to autostart

1. Start
2. Connect from the browser or another server
3. Enjoy!

## API

Message stucture:

```
{
  "code": {one of described below},  // string
  "message": {additional parameters, e.g. app version}, // string
  "base64img": {image formatted as base64 encoded string}, // string
}
```

Code types:
```
StartNAPS2 = 0111,    // Just starts NAPS2
OpenSettings = 0112,  // Opens Agent settings (not implemented yet)
JustScan = 1100,      // Starts NAPS2 or use already started, scans and closes NAPS
ScanAndWait = 1101,   // Starts NAPS2 or use already started, scans and do not close it
JustOpen = 1102,      // Starts NAPS2
BatchScan = 1103,     // Opens batch scan window
SendBitmap = 0211,    // Used by NAPS, sends image to all clients
IAMNAPS = 2211,       // Used by NAPS, sends to setup it as special client when connected
GetVer = 2101,        // Used by Agent, gets NAPS version or tells that it is not connected
MyVer = 2201,         // Used by NAPS, sends its version to all clients
GetConnectedNAPS = 3000 // Get NAPS version or tells that it is not connected
CloseNAPS = 3001      // Closes NAPS window
HideNAPS = 3002       // Hide NAPS window  (recommended when needed batch)
HideWithTaskBarNAPS = 3003  // Hide in taskbar (all windows)
ShowNAPS = 3004       // Shows NAPS window
ShowInTaskBarNAPS = 3005 // Enables in taskbar (all windows)
ScanStart = 0220,     // Fires on scan was started
ScanEnd = 0222,       // Fires on scan was ended
OnError = 0300        // Fires when catches error during scan
NAPSProgress = 4004,  // [NEW!] Fires when progress changes
NAPSError = 4003,     // Fires when error message box should appear
NAPSWarn = 4002,      // Fires when warn message box should appear
NAPSQuestion = 4001,  // Fires when question message box should appear
NAPSInfo = 4000,      // Fires when info message box should appear
```

Example usage is described in [example.cshtml](example.cshtml) file.