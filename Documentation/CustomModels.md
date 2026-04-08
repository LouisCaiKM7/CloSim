# Custom Models

<h2>In addition to the Part Library, Builder Beta features a dedicated system for adding external models.</h2>

### Build Assembly
* `Build Assembly` is a *dynamic* part library
* To add your part, copy the model into `Resouces/Parts/Assembly`
  * The model can be .dae (collada in OnShape), .fbx, or a .prefab
* Once added, the part will show up in the `BuildAssembly` script's dropdown menu

![image](Img/CustomModels/BuildAssembly.png)

### Usage
* Parts will autopopulate the `dropdown` and simply selecting them will spawn them
* `Scale Fix` -  Sometimes when models are downloaded from cad they are scaled down automatically. Enabling this setting will upscale the model to accurate sizing

### Prefab Support
* `Build Assembly` can spawn prefab files as well
* This allows an assembly to not only have a model, but `colliders` and `mechanisms` inside it as well

### Sharing
* You can follow the steps in [SharingRobots](SharingRobots.md) except only select your models and the parts in `Resouces/Parts/Assembly` to share individual assemblies

# [Further Reading](FurtherReading.md)
