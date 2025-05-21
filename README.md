# CrewMap
A simple to map and export a mesh of a space with the Magic Leap 2

The app creates an stl file with the mesh and a json file with coordinates of any detected Aruco codes around the space.

#Usage
- launch the app
- walk and look around to create the mesh
- press the trigger on the remote to save files to internal storage. The screen will go blank during the operation, when the mesh reapears the files are ready.
- download the files with usb file transfer from the application's folder.

#Building
Create new project with unity according to magicLeap documentation.
The dependancies are MagicLeapSDK
And pb_Stl https://github.com/balthazar-crew/pb_Stl.git