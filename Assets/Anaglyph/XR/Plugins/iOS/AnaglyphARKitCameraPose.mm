#import <ARKit/ARKit.h>
#include <string.h>

// The ARFrame's camera pose, column-major: in the sensor's fixed landscape orientation (the one its
// captured image has), and turned to the given interface orientation (diagnostics only).
// Unity's ScreenOrientation values 1-4 match UIInterfaceOrientation's.
extern "C" bool AnaglyphARKitCameraTransforms(void* arFrame, int screenOrientation, float* sensorColumnMajor, float* displayColumnMajor)
{
    if (arFrame == NULL) return false;
    ARFrame* frame = (__bridge ARFrame*)arFrame;
    ARCamera* camera = frame.camera;
    if (camera.trackingState == ARTrackingStateNotAvailable) return false;

    simd_float4x4 sensor = camera.transform;
    memcpy(sensorColumnMajor, &sensor, sizeof(float) * 16);

    UIInterfaceOrientation orientation = (UIInterfaceOrientation)screenOrientation;
    if (orientation < UIInterfaceOrientationPortrait || orientation > UIInterfaceOrientationLandscapeLeft)
        orientation = UIInterfaceOrientationLandscapeRight;
    simd_float4x4 display = simd_inverse([camera viewMatrixForOrientation:orientation]);
    memcpy(displayColumnMajor, &display, sizeof(float) * 16);
    return true;
}
