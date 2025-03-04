jp.keijiro.apriltag - AprilTag package for Unity
================================================

![gif](https://i.imgur.com/1iushmq.gif)

**jp.keijiro.apriltag** is a Unity package providing a native code
implementation of an AprilTag tracker.

**AprilTag** is a marker based tracking system developed by the APRIL Robotics
Laboratory at the University of Michigan. Please see [the AprilTag web page]
for further details.

[the AprilTag web page]: https://april.eecs.umich.edu/software/apriltag

System requirements
-------------------

- Unity 2021.3

At the moment, this package supports the following systems:

- Windows (x86-64), macOS (x86-64), Linux (x86-64), iOS (arm64), Android (arm64)

How to install
--------------

This package is available in the `Keijiro` scoped registry.

- Name: `Keijiro`
- URL: `https://registry.npmjs.com`
- Scope: `jp.keijiro`

Please follow [this gist] to add the registry to your project.

[this gist]: https://gist.github.com/keijiro/f8c7e8ff29bfe63d86b888901b82644c

How to try the sample project
-----------------------------

Clone this repository and play `DetectionTest.unity` on Unity Editor.

The current version of the `TagDetector` component only supports the
`tagStandard41h12` tag set. You can download those tag images from
[the apriltag-imgs repository]. Print some of them using a printer, or use a
smartphone screen to display the tags.

[the apriltag-imgs repository]:
  https://github.com/AprilRobotics/apriltag-imgs/tree/master/tagStandard41h12

The `DetectionTest` component uses the Field of View value of the main camera to
estimate tag positions. You can try the sample without adjusting it, but it may
give incorrect depth information. To get accurate tag positions, you should
match the FoV value with the actual camera FoV.

![image](https://i.imgur.com/BUVHSnXl.jpg)

For example, I'm using [Zoom Q2n-4K video camera] for testing, which gives about
78 degrees horizontal FoV at the mid-angle mode. So I changed the FOV Axis to
"Horizontal" and the Field of View value to 78.

[Zoom Q2n-4K video camera]:
  https://zoomcorp.com/en/us/video-recorders/video-recorders/q2n-4k-handy-video-recorder/

How to detect tags
------------------

At first, create the `AprilTag.TagDetector` object specifying the input image
dimensions. You can run the detector in a lower resolution by specifying a
decimation factor. It may improve the speed at the cost of accuracy and
detection rate.

```csharp
detector = new AprilTag.TagDetector(imageWidth, imageHeight, decimation);
```

Call the `ProcessImage` method every frame to detect tags from an input image.
You can use `ReadonlySpan<Color32>` to give an image. At the same time, you have
to specify the camera FoV (horizontal) in degrees and the tag size in meters.

```csharp
texture.GetPixels32(buffer);
detector.ProcessImage(buffer, fov, tagSize);
```

You can retrieve the detected tags from the `DetectedTags` property.

```csharp
foreach (var tag in detector.DetectedTags)
    Debug.Log($"{tag.ID} {tag.Position} {tag.Rotation}");
```

Dispose the detector object when you no longer need it.

```csharp
detector.Dispose();
```

For details, please check the [DetectionTest.cs] example.

[DetectionTest.cs]: /Assets/DetectionTest.cs

Related repositories
--------------------

- The original AprilTag project repository:
  https://github.com/AprilRobotics/apriltag
- Pre-generated tag images:
  https://github.com/AprilRobotics/apriltag-imgs
- A fork of the AprilTag repository used to build Unity plugin binaries.
  https://github.com/keijiro/apriltag
