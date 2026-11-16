#import <CoreHaptics/CoreHaptics.h>

static CHHapticEngine *engine;
static NSMutableArray<CHHapticPattern *> *patterns;
// the newest player of each pattern, kept alive until the pattern plays again
static NSMutableArray *latestPlayers;

static BOOL EnsureEngine()
{
    if (engine) return YES;
    if (!CHHapticEngine.capabilitiesForHardware.supportsHaptics) return NO;

    NSError *error = nil;
    engine = [[CHHapticEngine alloc] initAndReturnError:&error];
    if (!engine)
    {
        NSLog(@"[AnaglyphHaptics] no haptic engine: %@", error);
        return NO;
    }

    patterns = [NSMutableArray array];
    latestPlayers = [NSMutableArray array];
    engine.playsHapticsOnly = YES;
    engine.autoShutdownEnabled = YES;
    return YES;
}

// Returns a pattern index for AnaglyphHapticsPlay, or -1 when the device has no haptics or the AHAP is invalid.
extern "C" int AnaglyphHapticsRegister(const char *ahapJson)
{
    if (!EnsureEngine()) return -1;

    NSData *data = [NSData dataWithBytes:ahapJson length:strlen(ahapJson)];
    NSError *error = nil;
    NSDictionary *dictionary = [NSJSONSerialization JSONObjectWithData:data options:0 error:&error];
    CHHapticPattern *pattern = dictionary ? [[CHHapticPattern alloc] initWithDictionary:dictionary error:&error] : nil;
    if (!pattern)
    {
        NSLog(@"[AnaglyphHaptics] invalid pattern: %@", error);
        return -1;
    }

    [patterns addObject:pattern];
    [latestPlayers addObject:[NSNull null]];
    return (int)patterns.count - 1;
}

extern "C" void AnaglyphHapticsPlay(int index)
{
    if (!engine || index < 0 || index >= (int)patterns.count) return;

    // restarts the engine after auto shutdown, backgrounding or a reset; returns at once while it runs
    NSError *error = nil;
    if (![engine startAndReturnError:&error])
    {
        NSLog(@"[AnaglyphHaptics] engine did not start: %@", error);
        return;
    }

    // a fresh player per play: stopping and restarting one player at the same instant can drop the restart
    id<CHHapticPatternPlayer> player = [engine createPlayerWithPattern:patterns[index] error:&error];
    if (!player || ![player startAtTime:CHHapticTimeImmediate error:&error])
    {
        NSLog(@"[AnaglyphHaptics] pattern did not play: %@", error);
        return;
    }

    id previous = latestPlayers[index];
    if (previous != [NSNull null]) [previous stopAtTime:CHHapticTimeImmediate error:nil];
    latestPlayers[index] = player;
}
