# NAPS.Agent

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
CloseNAPS = 3001      // [NEW!] Closes NAPS window
HideNAPS = 3002       // [NEW!] Hide NAPS window  (recommended when needed batch)
HideWithTaskBarNAPS = 3003  // [NEW!] Hide in taskbar (all windows)
ShowNAPS = 3004       // [NEW!] Shows NAPS window
ShowInTaskBarNAPS = 3005 // [NEW!] Enables in taskbar (all windows)
ScanStart = 0220,     // Fires on scan was started
ScanEnd = 0222,       // Fires on scan was ended
OnError = 0300        // Fires when catches error during scan
NAPSError = 4003,     // Fires when error message box should appear
NAPSWarn = 4002,      // Fires when warn message box should appear
NAPSQuestion = 4001,  // Fires when question message box should appear
NAPSInfo = 4000,      // Fires when info message box should appear
```

Example usage is described in [example.cshtml](example.cshtml) file.