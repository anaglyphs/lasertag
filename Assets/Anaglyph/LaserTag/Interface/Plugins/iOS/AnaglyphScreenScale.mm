#import <UIKit/UIKit.h>

// Pixels per point on this screen: the size iOS lays its own interface out at.
extern "C" float AnaglyphScreenScale()
{
    return (float)[UIScreen mainScreen].nativeScale;
}
