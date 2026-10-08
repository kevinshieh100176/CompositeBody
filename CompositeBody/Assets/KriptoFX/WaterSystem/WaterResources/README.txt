Version 1.5.10

- Additional demo scenes  http://kripto289.com/AssetStore/WaterSystem/DemoScenes_1.5/
- My email is "kripto289@gmail.com"
- Discord channel https://discord.gg/GUUZ9D96Uq (you can get all new changes/fixes/features in the discord channel. The asset store version will only receive major updates)







-----------------------------------  WATER FIRST STEPS ------------------------------------------------------------------------------------------------------------

               1) Right click in hierarchy -> Effects -> Water system
               2) See the description of each setting: just click the help box with the symbol "?" or go over the cursor to any setting to see a text description. 

--------------------------------------------------------------------------------------------------------------------------------------------------------------------








-----------------------------------  DEMO SCENE CORRECT SETTINGS -----------------------------------------------------------------------------------------------
1) Use linear color space. Edit-> Project settings -> Player -> Other settings -> Color space -> Linear
If you use gamma space, then you need to change light intensity and water transparent/turbidity for better looking.
2) Import "cinemachine" (for camera motion)
Window -> Package Manager -> click button bellow "packages" tab -> select "All Packages" or "Packages: Unity registry" -> Cinemachine -> "Install"
----------------------------------------------------------------------------------------------------------------------------------------------------------------




----------------------------------- USING FLOWING EDITOR ---------------------------------------------------------------------------------------------------------
1) Click the "Flowmap Painter" button
2) Set the "Flowmap area position" and "Area Size" parameters. You must draw flowmap in this area!
3) Press and hold the left mouse button to draw on the flowmap area.
4) Use the "control" (ctrl) button + left mouse to erase mode.
5) Use the mouse wheel to change the brush size.
6) Press the "Save All" button.
7) All changes will be saved in the folder "Assets\KriptoFX\WaterSystem\WaterResources\Resources\SavedData\WaterID", so be careful and don't remove it.
You can see the current waterID under section "water->rendering tab". It's look like a "Water unique ID : Beach Rocky.M8W3ER5V"
----------------------------------------------------------------------------------------------------------------------------------------------------------------



----------------------------------- USING FLUIDS SIMULATION -------------------------------------------------------------------------------------------------
Fluids simulation calculate dynamic flow around static objects only.
1) draw the flow direction on the current flowmap (use flowmap painter)
2) save flowmap
3) press the button "Bake Fluids Obstacles"
-------------------------------------------------------------------------------------------------------------------------------------------------------------




----------------------------------- USING SHORELINE EDITOR ---------------------------------------------------------------------------------------------------------
1) Click the "Edit mode" button
2) Click the "Add Wave" button OR you can add waves to the mouse cursor position using the "Insert" key button. For removal, select a wave and press the "Delete" button.
3) You can use move/rotate/scale as usual for any other game object. 
4) Save all changes.
5) All changes will be saved in the folder "Assets\KriptoFX\WaterSystem\WaterResources\Resources\SavedData\WaterID", so be careful and don't remove it.
You can see the current waterID under section "water->rendering tab". It's look like a "Water unique ID : Beach Rocky.M8W3ER5V"
----------------------------------------------------------------------------------------------------------------------------------------------------------------



----------------------------------- USING RIVER SPLINE EDITOR ---------------------------------------------------------------------------------------------------------
1) In this mode, a river mesh is generated using splines (control points).
Press the button "Add River" and left click on your ground and set the starting point of your river
2) Press 
SHIFT + LEFT click to add a new point.
Ctrl + Left click deletes the selected point.
Use "scale tool" (or R button) to change the river width
3) A minimum of 3 points is required to create a river. Place the points approximately at the same distance and avoid strong curvature of the mesh 
(otherwise you will see red intersections gizmo and artifacts)
4) Press "Save Changes"
----------------------------------------------------------------------------------------------------------------------------------------------------------------




----------------------------------- USING ADDITIONAL FEATURES ---------------------------------------------------------------------------------------------------------
1) You can use the "water depth mask" feature (used for example for ignoring water rendering inside a boat). 
Just create a mesh mask and use shader "KriptoFX/Water/KW_WaterHoleMask"
2) For buoyancy, add the script "KW_Buoyancy" to your object with rigibody. 
3) For compatibility with third-party assets (Enviro/Azure/Atmospheric height fog/etc) use WaterSystem -> Rendering -> Third-party fog support
----------------------------------------------------------------------------------------------------------------------------------------------------------------



----------------------------------- WATER API --------------------------------------------------------------------------------------------------------------------------
1) To get the water position/velocity (for example for bouyancy) use follow code:


 //I use async readback from the compute shader, so actual data has 1+ frame delay. 
 //And the somes frames may not have actual data. 

//you can use WaterSurfaceRequestList/WaterSurfaceRequestArray/WaterSurfaceRequestPoint

 private WaterSurfaceRequestList _request = new WaterSurfaceRequestList();


 private void FixedUpdate()
 {
    _request.SetNewPositions(_waterWorldPos); 

    var isDataUpdated = WaterSystem.TryGetWaterSurfaceData(_request);
    if (!isDataUpdated)
    {
        //do something else :) 
        return;
    }

    var waterPos      = _request.Result[i].Position; //the "position.y" will be updated as soon as it is ready. 
    var velocity = _request.Result[i].Velocity;
 }




2) if you want to manually synchronize the time for all clients over the network, use follow code:
_waterInstance.UseNetworkTime = true;
_waterInstance.NetworkTime = ...  //your time in seconds

3) WaterInstance.IsWaterRenderingActive = true/false;   //You can manually control the rendering of water (software occlusion culling)
4) WaterInstance.WorldSpaceBounds   //world space bounds of the current quadtree mesh/custom mesh/river 
5) WaterInstance.IsCameraUnderwater() //check if the current rendered camera intersect underwater volume
For example, you can detect if your character enters the water to like triggering a swimming state.
6) Example of adding shoreline waves in realtime

var data =  WaterInstance.Settings.ShorelineWaves = ScriptableObject.CreateInstance<ShorelineWavesScriptableData>();
for(int i = 0; I < 100; i++)
{
  var wave = new ShorelineWave(typeID: 0, position, rotation, scale, timeOffset, flip);
  wave.UpdateMatrix();
  data.Waves.Add(wave);
}
WaterInstance.Settings.ShorelineWaves = data;
----------------------------------------------------------------------------------------------------------------------------------------------------------------






Other resources: 
Galleon https://sketchfab.com/Harry_L
Shark https://sketchfab.com/Ravenloop
Pool https://sketchfab.com/aurelien_martel









/////////////////////////////////// Release notes ///////////////////////////////////////////////////////
Relese notes 1.5.9b
-fixed memory leak with multiple camera
-fixed/improved buoyancy

Relese notes 1.5.9a
-fixed some compiller errors

Relese notes 1.5.8
- added a more accurate and faster way to get the position of the ocean. It allows you to get ~20 times higher accuracy. (but this method has a limitation and only works with global wind).
Also requires API changes, as data is now collected in a structured buffer. And to identify different positions, you need to use a WaterSurfaceRequest (see the readme for details)

private WaterSurfaceRequest _request = new WaterSurfaceRequest();

surfaceRequest.SetNewPositions(worldPositionsArray);
WaterSystem.TryGetWaterSurfaceData(_request);
_request.Result[index].position;

- added a "screen space skybox" property to prevent screen space from leaking through objects (such as trees)
- added volumetric lighting blur
- added "rsp" file to avoid unity warnings
- added water transparent lit/unlit shader ("KriptoFX/KWS/TransparentLit_Dithered"  "KriptoFX/KWS/TransparentUnlit")

- fixed underwater horizon artefacts
- fixed refraction depth issue
- fixed dlss with dx12 and hardware mode
- fixed some compiller issues on macos/ps5


Release notes 1.5.7
- fixed VR rendering
- fixed ps4/ps5/metal errors
-added new transparent behavior (after 50+ meters absorption will have non-physical behaviour)
-added new underwater override transparent offset
-dye color behavior improvement

Release notes 1.5.6
-added vertex extrude volume (Right click in hierarchy -> Effects -> KWS Water Extrude Volume)
- fixed build compiller errors in some cases
- fixed dynamic waves for rivers
- fixed additional lights caustic rendering
- fixed water surface depth issue at far distance (water surface looks like a staircase due to a depth buffer precission error)
- fxed VR rendering error when XR is not in use.
- minor fixes


Release notes 1.5.4
-fixed fluids simulation rendering
-fixed some issues with dynamic waves and shoreline waves


Release notes 1.5.3

New:

-added new Screen Space Reflection algorithm. Faster ~x2 times, less sky leaking, and works with non-planar surfaces like rivers or custom meshes
-volumetric lighting has been improved (physically correct absorption and scattering formulas, reduced outline artefacts, less noise and ghost effect)
-added cozy 3 support
-added orthographic support
-improved caustic rendering (no longer visible in the shadows and backface surfaces)
-added aquarium mode (right now internal reflection/volumetric volume lighting works correctly)
-added aquarium tension effect (you can achieve the same water effect like in "Ori" game)
-added depth masking for river simulation (in most cases this can speed up the simulation x2-x3 times)
-added reflection probe support (only two important reflection probes with blending, due unity limitations). New property in the tab WaterSystem->Reflection-> Use Reflection Probes
-added refraction resolution downscaling and underwater always works in full resolution
-added new internal reflection with volumetric fog (now the reflection/reflection under the water looks more physically correct)

Fixes:
-reduced volumetric lighting artifacts with big waves
-fixed waves rotation and incorrect waves choppiness.
-improved subsurface scattering
-removed some outdated UI parameters. For example "volumetric directional light intensity multiplier", similar parameters should be avoided because they violate the PBR principles and energy conservation (like self-lighted water).
-fixed memory leak with multiple water instances and fluid simulations.
-you can now select a folder when saving a water profile.
-fixed ps5 compiller errors.


Release Notes 1.5.01

New:

-Implemented large-scale optimizations for multiple water instances. Currently, all effects (global waves simulation, SSR reflection, buoyancy, dynamic waves, volumetric lightings, caustic, shoreline, ocean foam, etc.) are calculated once instead of per water instance. Exceptions include planar reflection and fluids simulation.

-added new fft waves generation (with lod system for the storm waves)
-added fft waves scaling mode
-added local/global wind settings
-added ocean foam rendering

-added dynamic waves interaction from meshes

-added volumetric lighting caustic parameters
-added volumetric lighting rendering using temporal reprojection instead of blur(bilateral/gaussian blur was removed)
-added volumetric lighting intensity settings for dir/additional lights

-added caustic rendering for additional lights (point/spot)
-added infinite caustic rendering without cascades

-added underwater half line effect
-added underwater physical approximated refraction and internal reflection (using SSR)

-added transform scale (mesh size property removed)
-added stencil mask (for example, you can look out the porthole, see the demo scene "UnderwaterShip")
-added wide-angle camera rendering for "unity recorder" and 360 degrees cubemaps (water -> rendering tab -> wide-angle toogle)

-Added new underwater bubbles/dust effects
-Added new water decals (lit/unlit) and effect (duckweed/blood)
-Added new particles decals (foam trails, etc)
-Added aura2 support
-Added underwater environment lighting attenuation depending on the depth and spot/point lights.

-Added the new water/underwater rendering using transparent queue (Water-> Rendering tab -> Transparent sorting priority)
(before it rendered only before or after transparent). Right now all third-party fog rendering (like enviro3) can work correctly and you can render transparent effects before/after water/underwater pass.

-added experimental "curved world plugin" support
(you need to open the file "KWS_PlatformSpecificHelpers.cginc" and uncomment this line "#define CURVED_WORLDS".
By default used "#define CURVEDWORLD_BEND_TYPE_LITTLEPLANET_Y"  for other modes you need to replace this line)

Improvements:

-improved waves aliasing/filtering
-improved refraction artefacts (outside the viewport)
-optimized quadtree rendering for non-squared meshes
-optimized quadtree mesh rendering for multiple cameras
-changed the logic of saving profiles for multiple instances

API changes:

-Optimized buoyancy API for mutliple points (WaterSustem.GetWaterSurfaceDataArray)
-Added new feature "WaterSystem.UseNetworkBuoyancy", it allow you to render buoyancy height data without cameras

-Added the new API for third-party shaders (hlsl/shadergraph)
Custom shaders using:
#pragma multicompile  KWS_USE_VOLUMETRIC_LIGHT
#include "Assets/KriptoFX/WaterSystem/WaterResources/Shaders/Resources/Common/KWS_SharedAPI.cginc"
Shadergraph using
Create node -> subgraph -> KWS_API_exampleNode

Fixes:

-fixed mac M1 compiler errors
-fixed xbox compiller errors
-fixed ps5 compiller errors
-fixed incorrect buoyancy height
-fixed rain time overflow
-fixed finite mesh quadtree patch holes
-fixed some warnings relative to textures (depth/color etc)
-fixed vr rendering issues
-fixed caustic dispersion
-fixed multiple camera rendering
-fixed spline editor bugs
-fixed incorrect river buoyancy
-fixed incorrect custom importer and incorrect GUID of flowmaps
-fixed incorrect spline bounding box
-fixed dynamic waves for multiple cameras
-fixed HDRI skybox encoding
-fixed quadtree mesh rendering with multiple cameras and different settings (far/fov, etc)

-Numerous other fixes and improvements.