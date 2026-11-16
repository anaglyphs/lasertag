#import <CoreHaptics/CoreHaptics.h>

static CHHapticEngine *engine;
static NSMutableArray<CHHapticPattern *> *patterns;
// NSNull until a pattern first plays, and again after the engine resets
static NSMutableArray *players;

static void ForgetPlayers()
{
    for (NSUInteger i = 0; i < players.count; i++) players[i] = [NSNull null];
}

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
    players = [NSMutableArray array];
    engine.playsHapticsOnly = YES;
    engine.autoShutdownEnabled = YES;
    engine.resetHandler = ^{
        dispatch_async(dispatch_get_main_queue(), ^{ ForgetPlayers(); });
    };
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
    [players addObject:[NSNull null]];
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

    id<CHHapticPatternPlayer> player = players[index];
    if ((id)player == [NSNull null])
    {
        player = [engine createPlayerWithPattern:patterns[index] error:&error];
        if (!player) return;
        players[index] = player;
    }

    [player stopAtTime:CHHapticTimeImmediate error:nil];
    [player startAtTime:CHHapticTimeImmediate error:nil];
}
