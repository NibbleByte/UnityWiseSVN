# WiseSVN For Unity

Simple but powerful SVN Integration for Unity 3D utilizing [TortoiseSVN](https://tortoisesvn.net/) UI. A must have plugin if you use SVN as your version control system in your project.

## Installation:
* Github upm package (pointless if you use git?) - merge this to your `Packages/manifest.json`
```
{
  "dependencies": {
    "devlocker.versioncontrol.wisesvn": "https://github.com/NibbleByte/UnityWiseSVN.git#upm"
}
```
* Asset Store plugin: *Working on it*

## Prerequisites:
* Have installed SVN installed
  * Have installed SVN command line interface (CLI)
* (Optional) Have TortoiseSVN installed.

![SVN CLI](https://i.stack.imgur.com/ZquvH.png)

## Features:
* **Hooks up to Unity move and delete file operations and executes respective svn commands to stay in sync.**
  * **Handles meta files as well.**
  * Moving assets to unversioned folder will ask the user to add that folder to SVN as well.
  * Moving folders / files that have conflicts will be rejected.
  * Will work with other custom tools as long as they move / rename assets using Unity API.
* Provides assets context menu for manual SVN operations like commit, update, revert etc.
* **Show overlay svn status icons**
  * Show server changes that you need to update.
  * Show locked files by you and your colleges. 
* Minimal performance impact
* You don't have to leave Unity to do SVN chores.
* File operations *should* be cross-platform. TortoiseSVN menus are not.

*Check the screenshots below*

## Usage
Do your file operations in Unity and the plugin will handle the rest.

User SVN operations are available in the menu (or right-click on any asset): `Assets/SVN/...`

**WARNING: Never focus Unity while the project is updating in the background. Newly added asset guids may get corrupted in which case the Library folder needs to be deleted. <br />
Preferred workflow is to always work inside Unity - use the \"Assets/SVN/...\" menus. \"Assets/SVN/Update All\" will block Unity while updating, to avoid Unity processing assets at the same time. <br />
This is an issue with how Unity works, not the plugin iteself. Unity says its by "design".**

## Overlay Icons
* Unversioned <img src="./Assets/DevLocker/VersionControl/WiseSVN/Resources/Editor/SVNOverlayIcons/SVNUnversionedIcon.png" width="24">
* Modified <img src="./Assets/DevLocker/VersionControl/WiseSVN/Resources/Editor/SVNOverlayIcons/SVNModifiedIcon.png" width="24">
* Added <img src="./Assets/DevLocker/VersionControl/WiseSVN/Resources/Editor/SVNOverlayIcons/SVNAddedIcon.png" width="24">
* Deleted <img src="./Assets/DevLocker/VersionControl/WiseSVN/Resources/Editor/SVNOverlayIcons/SVNDeletedIcon.png" width="24">
* Conflict <img src="./Assets/DevLocker/VersionControl/WiseSVN/Resources/Editor/SVNOverlayIcons/SVNConflictIcon.png" width="24">
* Locked by me <img src="./Assets/DevLocker/VersionControl/WiseSVN/Resources/Editor/SVNOverlayIcons/Locks/SVNLockedHereIcon.png" width="16">
* Locked by others <img src="./Assets/DevLocker/VersionControl/WiseSVN/Resources/Editor/SVNOverlayIcons/Locks/SVNLockedOtherIcon.png" width="16">
* Server has changes, update <img src="./Assets/DevLocker/VersionControl/WiseSVN/Resources/Editor/SVNOverlayIcons/Others/SVNRemoteChangesIcon.png" width="16">

## Screenshots
![OverlayIcons1](Docs/Screenshots/WiseSVN-OverlayIcons-Shot.png)
![OverlayIcons2](Docs/Screenshots/WiseSVN-OverlayIcons2-Shot.png)

![ContextMenu](Docs/Screenshots/WiseSVN-ContextMenu-Shot.png)
![File Operations](Docs/Screenshots/WiseSVN-Rename-Shot.png)
![Preferences](Docs/Screenshots/WiseSVN-Preferences-Shot.png)