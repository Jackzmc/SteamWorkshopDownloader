# SteamWorkshopDownloader

This is a tool that lets you bulk download workshop entries via SteamCMD, including workshop collections. This was tested with Cities Skylines.

## Notes

* SteamCMD will prompt for 2FA in the new window, you'll have to enter that manually
* SteamCMD will not quit automatically, because the program does not know if it failed to download anything, so check that it worked
* SteamCMD will automatically download in the folder the exe is in
* Steam login data (warning: plaintext) and download directory are stored in a settings.ini file in the folder the exe is in
