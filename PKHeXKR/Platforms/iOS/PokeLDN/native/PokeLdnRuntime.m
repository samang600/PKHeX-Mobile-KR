// Native iOS host for the existing PokeLDN 0.32.0 Python runner.
// BLE is bridged to the same socket:// transport supported by the Android-bundled PokeLDN code.
#import <Foundation/Foundation.h>
#import <CoreBluetooth/CoreBluetooth.h>
#import <Python/Python.h>
#include <stdbool.h>
#include <arpa/inet.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <sys/socket.h>
#include <unistd.h>

static const int RADIO_PORT = 47811;
static NSString *const RADIO_SERVICE = @"7a1e0101-5d2c-4c3e-9f4b-504f4b454c44";
static NSString *const RADIO_RX = @"7a1e0102-5d2c-4c3e-9f4b-504f4b454c44";
static NSString *const RADIO_TX = @"7a1e0103-5d2c-4c3e-9f4b-504f4b454c44";

@interface PKLdnRadio : NSObject <CBCentralManagerDelegate, CBPeripheralDelegate>
@property(nonatomic, strong) CBCentralManager *central;
@property(nonatomic, strong) CBPeripheral *board;
@property(nonatomic, strong) CBCharacteristic *rx;
@property(nonatomic, strong) NSMutableData *pending;
@property(nonatomic, strong) dispatch_queue_t queue;
@property(atomic) int client;
- (void)start;
- (void)setState:(NSString *)state;
- (void)scanAgain;
- (void)drain;
- (void)dropClient;
- (void)listen;
- (NSString *)state;
@end

@implementation PKLdnRadio

- (instancetype)init
{
    if ((self = [super init])) {
        _client = -1;
        _pending = [NSMutableData data];
        _queue = dispatch_queue_create("pkhexkr.pokeldn.radio", DISPATCH_QUEUE_SERIAL);
        [self setState:@"off"];
    }
    return self;
}

- (NSString *)state
{
    NSString *path = [NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES)[0]
        stringByAppendingPathComponent:@"ldn/.radio"];
    return [NSString stringWithContentsOfFile:path encoding:NSUTF8StringEncoding error:nil] ?: @"off";
}

- (void)setState:(NSString *)state
{
    NSString *home = [NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES)[0]
        stringByAppendingPathComponent:@"ldn"];
    [NSFileManager.defaultManager createDirectoryAtPath:home withIntermediateDirectories:YES attributes:nil error:nil];
    [state writeToFile:[home stringByAppendingPathComponent:@".radio"] atomically:YES
              encoding:NSUTF8StringEncoding error:nil];
}

- (void)start
{
    if (self.central) return;
    // 상태 복원 식별자: 백그라운드에서 iOS가 앱을 내렸다 다시 올려도 연결된 보드를 돌려받음 (UIBackgroundModes bluetooth-central)
    self.central = [[CBCentralManager alloc] initWithDelegate:self queue:self.queue
                    options:@{CBCentralManagerOptionRestoreIdentifierKey: @"pkhexkr.pokeldn.radio"}];
    [NSThread detachNewThreadSelector:@selector(listen) toTarget:self withObject:nil];
}

- (void)centralManager:(CBCentralManager *)central willRestoreState:(NSDictionary<NSString *, id> *)dict
{
    // 백그라운드에서 되살아난 경우: 이미 연결돼 있던 보드를 다시 맡음
    NSArray<CBPeripheral *> *peripherals = dict[CBCentralManagerRestoredStatePeripheralsKey];
    for (CBPeripheral *p in peripherals) {
        if (![p.name isEqualToString:@"POKELDN-RADIO"] && self.board) continue;
        self.board = p; p.delegate = self;
        if (p.state == CBPeripheralStateConnected) {
            [self setState:@"connecting"];
            [p discoverServices:@[[CBUUID UUIDWithString:RADIO_SERVICE]]];
        }
    }
}

- (void)centralManagerDidUpdateState:(CBCentralManager *)central
{
    if (central.state == CBManagerStatePoweredOn) {
        if (self.board && self.board.state == CBPeripheralStateConnected) return;   // 상태 복원으로 이미 연결된 보드가 있음
        [self setState:@"scanning"];
        // The radio firmware advertises its name without the service UUID (so scanning only works in the foreground;
        // once connected, bluetooth-central keeps the link alive in the background).
        [central scanForPeripheralsWithServices:nil options:nil];
    } else if (central.state == CBManagerStateUnauthorized) {
        [self setState:@"permission"];
    } else if (central.state == CBManagerStatePoweredOff) {
        [self setState:@"bluetooth_off"];
    } else if (central.state == CBManagerStateUnsupported) {
        [self setState:@"unsupported"];
    } else {
        [self setState:@"scanning"];
    }
}

- (void)centralManager:(CBCentralManager *)central didDiscoverPeripheral:(CBPeripheral *)peripheral
     advertisementData:(NSDictionary<NSString *, id> *)advertisementData RSSI:(NSNumber *)RSSI
{
    NSString *name = advertisementData[CBAdvertisementDataLocalNameKey] ?: peripheral.name;
    if (![name isEqualToString:@"POKELDN-RADIO"]) return;
    [central stopScan];
    self.board = peripheral;
    [self setState:@"connecting"];
    peripheral.delegate = self;
    [central connectPeripheral:peripheral options:nil];
}

- (void)centralManager:(CBCentralManager *)central didConnectPeripheral:(CBPeripheral *)peripheral
{
    [peripheral discoverServices:@[[CBUUID UUIDWithString:RADIO_SERVICE]]];
}

- (void)centralManager:(CBCentralManager *)central didFailToConnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error
{
    [self scanAgain];
}

- (void)centralManager:(CBCentralManager *)central didDisconnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error
{
    self.rx = nil;
    self.board = nil;
    [self dropClient];
    [self scanAgain];
}

- (void)scanAgain
{
    if (self.central.state != CBManagerStatePoweredOn) return;
    [self setState:@"scanning"];
    [self.central scanForPeripheralsWithServices:nil options:nil];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverServices:(NSError *)error
{
    if (error) { [self.central cancelPeripheralConnection:peripheral]; return; }
    BOOL found = NO;
    for (CBService *service in peripheral.services) {
        if ([service.UUID isEqual:[CBUUID UUIDWithString:RADIO_SERVICE]]) {
            found = YES;
            [peripheral discoverCharacteristics:@[[CBUUID UUIDWithString:RADIO_RX], [CBUUID UUIDWithString:RADIO_TX]] forService:service];
        }
    }
    if (!found) [self.central cancelPeripheralConnection:peripheral];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverCharacteristicsForService:(CBService *)service error:(NSError *)error
{
    if (error) { [self.central cancelPeripheralConnection:peripheral]; return; }
    for (CBCharacteristic *characteristic in service.characteristics) {
        if ([characteristic.UUID isEqual:[CBUUID UUIDWithString:RADIO_RX]]) self.rx = characteristic;
        if ([characteristic.UUID isEqual:[CBUUID UUIDWithString:RADIO_TX]])
            [peripheral setNotifyValue:YES forCharacteristic:characteristic];
    }
    if (!self.rx) [self.central cancelPeripheralConnection:peripheral];
}

- (void)peripheral:(CBPeripheral *)peripheral didUpdateNotificationStateForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error
{
    if (!error && characteristic.isNotifying && self.rx) {
        [self setState:@"ready"];
        [self drain];   // send anything the runner wrote while the link was still being set up
    }
    else if (error) [self.central cancelPeripheralConnection:peripheral];
}

- (void)peripheral:(CBPeripheral *)peripheral didUpdateValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error
{
    int fd = self.client;
    NSData *value = characteristic.value;
    if (error || fd < 0 || !value.length) return;
    const uint8_t *bytes = value.bytes;
    size_t remaining = value.length;
    while (remaining) {
        ssize_t sent = send(fd, bytes, remaining, 0);
        if (sent <= 0) { [self dropClient]; return; }
        bytes += sent;
        remaining -= (size_t)sent;
    }
}

- (void)drain
{
    if (!self.board || !self.rx) return;
    NSUInteger piece = [self.board maximumWriteValueLengthForType:CBCharacteristicWriteWithoutResponse];
    while (self.pending.length && self.board.canSendWriteWithoutResponse) {
        NSUInteger count = MIN(piece, self.pending.length);
        NSData *part = [self.pending subdataWithRange:NSMakeRange(0, count)];
        [self.board writeValue:part forCharacteristic:self.rx type:CBCharacteristicWriteWithoutResponse];
        [self.pending replaceBytesInRange:NSMakeRange(0, count) withBytes:NULL length:0];
    }
}

- (void)peripheralIsReadyToSendWriteWithoutResponse:(CBPeripheral *)peripheral { [self drain]; }

- (void)dropClient
{
    int fd = self.client;
    self.client = -1;
    if (fd >= 0) close(fd);
}

- (void)listen
{
    int server = socket(AF_INET, SOCK_STREAM, 0);
    if (server < 0) return;
    int on = 1;
    setsockopt(server, SOL_SOCKET, SO_REUSEADDR, &on, sizeof(on));
    struct sockaddr_in address = {.sin_family = AF_INET, .sin_port = htons(RADIO_PORT), .sin_addr.s_addr = htonl(INADDR_LOOPBACK)};
    if (bind(server, (struct sockaddr *)&address, sizeof(address)) || listen(server, 1)) { close(server); return; }
    for (;;) {
        int fd = accept(server, NULL, NULL);
        if (fd < 0) continue;
        @autoreleasepool {
        setsockopt(fd, IPPROTO_TCP, TCP_NODELAY, &on, sizeof(on));
        dispatch_sync(self.queue, ^{ [self dropClient]; [self.pending setLength:0]; self.client = fd; });
        uint8_t chunk[4096];
        ssize_t count;
        while ((count = recv(fd, chunk, sizeof(chunk), 0)) > 0) {
            @autoreleasepool {
            NSData *data = [NSData dataWithBytes:chunk length:(NSUInteger)count];
            dispatch_async(self.queue, ^{
                if (self.client != fd) return;
                [self.pending appendData:data];
                [self drain];
            });
            }
        }
        dispatch_async(self.queue, ^{ if (self.client == fd) [self dropClient]; });
        }
    }
}

@end

static PKLdnRadio *gRadio;
static BOOL gPythonReady = NO;
// Every string returned to C# is its own malloc copy, freed by pkhex_pokeldn_free.
// (A shared buffer let the UI thread's radio_state free the text the poll thread was still reading.)
static NSString *gInitError = @"";

static void SetInitError(NSString *message)
{
    gInitError = message.length ? message : @"Unknown PokeLDN runtime initialization error";
    NSLog(@"[PokeLDN] %@", gInitError);
}

static NSString *PythonExceptionText(void)
{
    if (!PyErr_Occurred()) return @"Python initialization failed without a Python exception";
    PyObject *type = NULL, *value = NULL, *traceback = NULL;
    PyErr_Fetch(&type, &value, &traceback);
    PyErr_NormalizeException(&type, &value, &traceback);
    PyObject *text = value ? PyObject_Str(value) : NULL;
    const char *utf8 = text ? PyUnicode_AsUTF8(text) : NULL;
    NSString *message = utf8 ? [NSString stringWithUTF8String:utf8] : @"Unable to format Python exception";
    Py_XDECREF(text); Py_XDECREF(type); Py_XDECREF(value); Py_XDECREF(traceback);
    PyErr_Clear();
    return message;
}

static NSString *DocumentsPath(void)
{
    return NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, YES)[0];
}

static void PythonError(void)
{
    if (PyErr_Occurred()) PyErr_Print();
}

static PyObject *CallPythonLocked(const char *function, const char *argument)
{
    PyObject *module = PyImport_ImportModule("ios_host");
    PyObject *callable = module ? PyObject_GetAttrString(module, function) : NULL;
    PyObject *arg = argument ? PyUnicode_FromString(argument) : NULL;
    PyObject *result = callable ? (argument ? PyObject_CallOneArg(callable, arg) : PyObject_CallNoArgs(callable)) : NULL;
    Py_XDECREF(arg); Py_XDECREF(callable); Py_XDECREF(module);
    if (!result) PythonError();
    return result;
}

static bool CallPythonBool(const char *function, const char *argument)
{
    if (!gPythonReady) return false;
    PyGILState_STATE state = PyGILState_Ensure();
    PyObject *value = CallPythonLocked(function, argument);
    int ok = value ? PyObject_IsTrue(value) : 0;
    Py_XDECREF(value);
    PyGILState_Release(state);
    return ok == 1;
}

static const char *CallPythonString(const char *function, const char *argument)
{
    if (!gPythonReady) return strdup("");
    PyGILState_STATE state = PyGILState_Ensure();
    PyObject *value = CallPythonLocked(function, argument);
    PyObject *text = value ? PyObject_Str(value) : NULL;
    const char *utf8 = text ? PyUnicode_AsUTF8(text) : NULL;
    char *copy = strdup(utf8 ?: "");
    Py_XDECREF(text); Py_XDECREF(value);
    PyGILState_Release(state);
    return copy;
}

__attribute__((visibility("default"))) bool pkhex_pokeldn_init(void)
{
    if (gPythonReady) return YES;
    gInitError = @"";
    gRadio = [PKLdnRadio new];
    [gRadio start];
    NSString *resources = NSBundle.mainBundle.resourcePath;
    NSString *documents = DocumentsPath();
    NSString *data = [documents stringByAppendingPathComponent:@"ldn/pokeldn"];
    [NSFileManager.defaultManager createDirectoryAtPath:data withIntermediateDirectories:YES attributes:nil error:nil];
    setenv("POKELDN_PLATFORM", "ios", 1);
    setenv("POKELDN_IOS_BUNDLE", resources.UTF8String, 1);
    setenv("POKELDN_IOS_DOCUMENTS", documents.UTF8String, 1);
    setenv("POKELDN_IOS_RADIO", "socket://127.0.0.1:47811", 1);
    setenv("POKELDN_DATA", data.UTF8String, 1);
    setenv("POKELDN_RADIO", "esp32:socket://127.0.0.1:47811", 1);

    NSString *pythonHome = [resources stringByAppendingPathComponent:@"python"];
    PyPreConfig preconfig; PyConfig config;
    PyPreConfig_InitIsolatedConfig(&preconfig); PyConfig_InitIsolatedConfig(&config);
    preconfig.utf8_mode = 1; config.buffered_stdio = 0; config.write_bytecode = 0; config.install_signal_handlers = 0;
    PyStatus status = Py_PreInitialize(&preconfig);
    if (PyStatus_Exception(status)) {
        SetInitError([NSString stringWithFormat:@"Py_PreInitialize: %s", status.err_msg ?: "unknown error"]);
        return NO;
    }
    wchar_t *home = Py_DecodeLocale(pythonHome.UTF8String, NULL);
    PyConfig_SetString(&config, &config.home, home); PyMem_RawFree(home);
    config.module_search_paths_set = 1;
    NSArray<NSString *> *paths = @[
        [pythonHome stringByAppendingPathComponent:@"lib/python3.13"],
        [pythonHome stringByAppendingPathComponent:@"lib/python3.13/lib-dynload"],
        [resources stringByAppendingPathComponent:@"pokeldn/app"],
        [resources stringByAppendingPathComponent:@"pokeldn/app_packages"]
    ];
    for (NSString *path in paths) {
        wchar_t *entry = Py_DecodeLocale(path.UTF8String, NULL);
        PyWideStringList_Append(&config.module_search_paths, entry); PyMem_RawFree(entry);
    }
    status = Py_InitializeFromConfig(&config); PyConfig_Clear(&config);
    if (PyStatus_Exception(status)) {
        SetInitError([NSString stringWithFormat:@"Py_InitializeFromConfig: %s", status.err_msg ?: "unknown error"]);
        return NO;
    }
    // Only the read-only app bundle is on sys.path. Documents is visible in the Files app
    // (UIFileSharingEnabled), so a module dropped there must never be importable.
    int bootstrap = PyRun_SimpleString("import ios_bootstrap\nios_bootstrap.install()\n");
    if (bootstrap != 0) {
        SetInitError([NSString stringWithFormat:@"iOS crypto bootstrap: %@", PythonExceptionText()]);
        return NO;
    }
    PyObject *module = PyImport_ImportModule("ios_host");
    if (!module) {
        SetInitError([NSString stringWithFormat:@"Import ios_host: %@", PythonExceptionText()]);
        return NO;
    }
    Py_DECREF(module);
    PyEval_SaveThread();
    gPythonReady = YES;
    return YES;
}

__attribute__((visibility("default"))) const char *pkhex_pokeldn_error(void)
{
    return strdup(gInitError.UTF8String ?: "");
}

__attribute__((visibility("default"))) bool pkhex_pokeldn_start(const char *request)
{
    [gRadio start];
    return CallPythonBool("start", request ?: "{}");
}

__attribute__((visibility("default"))) const char *pkhex_pokeldn_poll(void)
{
    return CallPythonString("poll", NULL);
}

__attribute__((visibility("default"))) bool pkhex_pokeldn_busy(void)
{
    return CallPythonBool("busy", NULL);
}

__attribute__((visibility("default"))) void pkhex_pokeldn_cancel(void)
{
    if (!gPythonReady) return;
    PyGILState_STATE state = PyGILState_Ensure();
    Py_XDECREF(CallPythonLocked("cancel", NULL));
    PyGILState_Release(state);
}

__attribute__((visibility("default"))) const char *pkhex_pokeldn_radio_state(void)
{
    const char *value = gRadio ? gRadio.state.UTF8String : "off";
    return strdup(value ?: "off");
}

__attribute__((visibility("default"))) void pkhex_pokeldn_free(const char *value)
{
    free((void *)value);
}
