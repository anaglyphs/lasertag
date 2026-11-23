#import <Foundation/Foundation.h>
#import <ifaddrs.h>
#import <net/if.h>

// iOS has no public call for the Wi-Fi switch. The peer-to-peer Wi-Fi interface awdl0 is up exactly while Wi-Fi is on.
static const char *const PeerToPeerWifiInterfaceName = "awdl0";

extern "C" int AnaglyphIsWifiEnabled()
{
    struct ifaddrs *interfaces = NULL;
    if (getifaddrs(&interfaces) != 0) return 1;

    BOOL up = NO;
    for (struct ifaddrs *interface = interfaces; interface != NULL && !up; interface = interface->ifa_next)
        up = strcmp(interface->ifa_name, PeerToPeerWifiInterfaceName) == 0 && (interface->ifa_flags & IFF_UP);
    freeifaddrs(interfaces);
    return up;
}
