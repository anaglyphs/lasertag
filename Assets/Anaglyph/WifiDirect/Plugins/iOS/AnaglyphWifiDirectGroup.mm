#import <NetworkExtension/NetworkExtension.h>
#import <arpa/inet.h>
#import <ifaddrs.h>
#import <net/if.h>

// Joins a Wi-Fi Direct group as an ordinary Wi-Fi network, which is all an iPhone can do with one.
// iOS asks the user before joining, and returns to the earlier network when the group is left or the app closes.
// Needs the Hotspot Configuration entitlement.

static const int Idle = 0;
static const int Forming = 1;
static const int Ready = 2;
static const int Failed = 3;

// every Android group owner hands out addresses in this range
static NSString *const GroupAddressPrefix = @"192.168.49.";
static NSString *const WifiInterfaceName = @"en0";

static int state = Idle;
static BOOL networkApplied;
static BOOL userRefused;
static NSString *joinedNetworkName;
// every join and leave starts a new attempt, so the late answer of an earlier one is ignored
static int attempt;

static BOOL HasGroupAddress()
{
    struct ifaddrs *interfaces = NULL;
    if (getifaddrs(&interfaces) != 0) return NO;

    BOOL found = NO;
    for (struct ifaddrs *interface = interfaces; interface != NULL && !found; interface = interface->ifa_next)
    {
        if (interface->ifa_addr == NULL || interface->ifa_addr->sa_family != AF_INET) continue;
        if (![WifiInterfaceName isEqualToString:@(interface->ifa_name)]) continue;

        char text[INET_ADDRSTRLEN];
        inet_ntop(AF_INET, &((struct sockaddr_in *)interface->ifa_addr)->sin_addr, text, sizeof(text));
        found = [@(text) hasPrefix:GroupAddressPrefix];
    }
    freeifaddrs(interfaces);
    return found;
}

extern "C" void AnaglyphWifiDirectGroupLeave()
{
    attempt++;
    state = Idle;
    networkApplied = NO;
    userRefused = NO;
    if (!joinedNetworkName) return;
    [NEHotspotConfigurationManager.sharedManager removeConfigurationForSSID:joinedNetworkName];
    joinedNetworkName = nil;
}

extern "C" void AnaglyphWifiDirectGroupJoin(const char *networkName, const char *passphrase)
{
    AnaglyphWifiDirectGroupLeave();
    int thisAttempt = attempt;
    state = Forming;
    joinedNetworkName = @(networkName);

    NEHotspotConfiguration *network =
        [[NEHotspotConfiguration alloc] initWithSSID:joinedNetworkName passphrase:@(passphrase) isWEP:NO];
    network.joinOnce = YES;

    [NEHotspotConfigurationManager.sharedManager applyConfiguration:network completionHandler:^(NSError *error) {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (thisAttempt != attempt) return;
            if (!error || error.code == NEHotspotConfigurationErrorAlreadyAssociated)
            {
                networkApplied = YES;
                return;
            }
            NSLog(@"[AnaglyphWifiDirectGroup] join failed: %@", error);
            userRefused = error.code == NEHotspotConfigurationErrorUserDenied;
            state = Failed;
        });
    }];
}

// Ready once the phone has its address inside the group. iOS reports a join as done before that, and even when it failed.
extern "C" int AnaglyphWifiDirectGroupState()
{
    if (state == Forming && networkApplied && HasGroupAddress()) state = Ready;
    return state;
}

extern "C" int AnaglyphWifiDirectGroupUserRefused()
{
    return userRefused;
}
