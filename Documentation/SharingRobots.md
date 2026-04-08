<h1>Sharing Robots</h1>

<h2>Saving Your Robot</h2>

* Start by locating the robot's parent folder (`Resources/Robots`)
* Right-click on the robot you want to share and select `Export Package`
* Deselect the `Include dependencies` box on the bottom of the window that opens up. This should leave just the robot
  you want to save and the filepath to it
  ![image](Img/SharingRobots/Export%20Image.png)
* Click export and save the file however you'd like
* That's it! Your robot is now saved as a unity package that can be transferred to other users

<h2>Uploading a Robot</h2>

* Download the `.unitypackage` robot file
* Locate the robots folder (`Resources/Robots`)
* Right-click an empty spot in the `Project Window` and select `Import Package -> Custom Package...`
* Locate and select the `.unitypackage` robot file. This should open up the import window
* In the import window, ensure that the robot prefab is selected and click `Import`
  ![image](Img/SharingRobots/Import%20Image.png)
* It's that simple! Your robot is now uploaded. To start playing with it, select it in the `GameManager` like any other
  robot and start playing. Don't forget that any robots you upload can still be edited as normal, so don't be afraid to
  change settings such as keybinds to your liking

# [Further Reading](FurtherReading.md)