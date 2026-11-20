#import <CoreBluetooth/CoreBluetooth.h>

// Advertises one 128-bit BLE service UUID and collects the service UUIDs of nearby advertisers that start with a prefix.
// Both managers run on the main queue, which is also the thread Unity calls from.
@interface AnaglyphBluetoothBeacon : NSObject <CBCentralManagerDelegate, CBPeripheralManagerDelegate>
@property (nonatomic, strong) CBCentralManager *central;
@property (nonatomic, strong) CBPeripheralManager *peripheral;
@property (nonatomic, copy) NSString *advertisedServiceUuid;
@property (nonatomic, copy) NSString *scannedPrefixHex;
@property (nonatomic, strong) NSMutableOrderedSet<NSString *> *unreadSightings;
@end

static NSString *HexDigits(NSString *uuidText)
{
    return [uuidText stringByReplacingOccurrencesOfString:@"-" withString:@""].lowercaseString;
}

@implementation AnaglyphBluetoothBeacon

+ (AnaglyphBluetoothBeacon *)shared
{
    static AnaglyphBluetoothBeacon *beacon;
    if (!beacon)
    {
        beacon = [AnaglyphBluetoothBeacon new];
        beacon.unreadSightings = [NSMutableOrderedSet orderedSet];
    }
    return beacon;
}

- (BOOL)startAdvertising:(NSString *)serviceUuid
{
    // creating the manager is what makes iOS ask for Bluetooth access
    if (!self.peripheral) self.peripheral = [[CBPeripheralManager alloc] initWithDelegate:self queue:nil];
    if (self.peripheral.state != CBManagerStatePoweredOn)
    {
        self.advertisedServiceUuid = nil;
        return NO;
    }
    if ([serviceUuid isEqualToString:self.advertisedServiceUuid]) return YES;

    [self.peripheral stopAdvertising];
    [self.peripheral startAdvertising:@{CBAdvertisementDataServiceUUIDsKey: @[[CBUUID UUIDWithString:serviceUuid]]}];
    self.advertisedServiceUuid = serviceUuid;
    return YES;
}

- (void)stopAdvertising
{
    if (!self.advertisedServiceUuid) return;
    [self.peripheral stopAdvertising];
    self.advertisedServiceUuid = nil;
}

- (BOOL)startScanning:(NSString *)serviceUuidPrefix
{
    if (!self.central) self.central = [[CBCentralManager alloc] initWithDelegate:self queue:nil];
    if (self.central.state != CBManagerStatePoweredOn)
    {
        [self forgetScanning];
        return NO;
    }
    NSString *prefixHex = HexDigits(serviceUuidPrefix);
    if (self.central.isScanning && [prefixHex isEqualToString:self.scannedPrefixHex]) return YES;

    // a scan can only be narrowed to whole UUIDs, so every advertiser is heard and the prefix is matched here
    [self.central scanForPeripheralsWithServices:nil options:@{CBCentralManagerScanOptionAllowDuplicatesKey: @YES}];
    self.scannedPrefixHex = prefixHex;
    return YES;
}

- (void)stopScanning
{
    if (!self.scannedPrefixHex) return;
    if (self.central.state == CBManagerStatePoweredOn) [self.central stopScan];
    [self forgetScanning];
}

- (void)forgetScanning
{
    self.scannedPrefixHex = nil;
    [self.unreadSightings removeAllObjects];
}

- (void)centralManagerDidUpdateState:(CBCentralManager *)central
{
}

- (void)peripheralManagerDidUpdateState:(CBPeripheralManager *)peripheral
{
}

- (void)peripheralManagerDidStartAdvertising:(CBPeripheralManager *)peripheral error:(NSError *)error
{
    if (!error) return;
    NSLog(@"[AnaglyphBluetoothBeacon] advertising failed: %@", error);
    self.advertisedServiceUuid = nil;
}

- (void)centralManager:(CBCentralManager *)central didDiscoverPeripheral:(CBPeripheral *)peripheral
     advertisementData:(NSDictionary<NSString *, id> *)advertisementData RSSI:(NSNumber *)RSSI
{
    if (!self.scannedPrefixHex) return;

    for (NSString *key in @[CBAdvertisementDataServiceUUIDsKey, CBAdvertisementDataOverflowServiceUUIDsKey])
    {
        for (CBUUID *serviceUuid in advertisementData[key])
        {
            NSString *uuid = serviceUuid.UUIDString.lowercaseString;
            if ([HexDigits(uuid) hasPrefix:self.scannedPrefixHex]) [self.unreadSightings addObject:uuid];
        }
    }
}

@end

// Returns 1 while the advertisement for this UUID is running or starting. Safe to repeat.
extern "C" int AnaglyphBluetoothBeaconStartAdvertising(const char *serviceUuid)
{
    return [AnaglyphBluetoothBeacon.shared startAdvertising:[NSString stringWithUTF8String:serviceUuid]];
}

extern "C" void AnaglyphBluetoothBeaconStopAdvertising()
{
    [AnaglyphBluetoothBeacon.shared stopAdvertising];
}

// Returns 1 while the scan for this prefix is running. Safe to repeat. The prefix is the UUID's leading characters.
extern "C" int AnaglyphBluetoothBeaconStartScanning(const char *serviceUuidPrefix)
{
    return [AnaglyphBluetoothBeacon.shared startScanning:[NSString stringWithUTF8String:serviceUuidPrefix]];
}

extern "C" void AnaglyphBluetoothBeaconStopScanning()
{
    [AnaglyphBluetoothBeacon.shared stopScanning];
}

// Copies one service UUID heard since the last call into the buffer and returns its length, or 0 when none is left.
// Each UUID comes out once however often it was heard.
extern "C" int AnaglyphBluetoothBeaconTakeSighting(char *buffer, int capacity)
{
    NSMutableOrderedSet<NSString *> *unread = AnaglyphBluetoothBeacon.shared.unreadSightings;
    if (unread.count == 0) return 0;

    NSString *uuid = unread.firstObject;
    [unread removeObjectAtIndex:0];

    int length = (int)[uuid lengthOfBytesUsingEncoding:NSUTF8StringEncoding];
    if (length > capacity) return 0;
    memcpy(buffer, uuid.UTF8String, length);
    return length;
}
