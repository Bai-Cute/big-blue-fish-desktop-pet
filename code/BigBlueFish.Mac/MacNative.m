#import <AppKit/AppKit.h>
#import <Security/Security.h>
#include <stdlib.h>
#include <string.h>

// One app-scoped generic password. The secret never enters preferences.json.
static NSDictionary *chatKeychainQuery(void) {
    return @{ (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
              (__bridge id)kSecAttrService: @"io.github.bai-cute.bigbluefish.mac",
              (__bridge id)kSecAttrAccount: @"chat-api-key" };
}

int bbf_keychain_read(char **value) {
    if (!value) return errSecParam;
    *value = NULL;
    NSMutableDictionary *query = [chatKeychainQuery() mutableCopy];
    query[(__bridge id)kSecReturnData] = @YES;
    query[(__bridge id)kSecMatchLimit] = (__bridge id)kSecMatchLimitOne;
    CFTypeRef result = NULL;
    OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, &result);
    if (status != errSecSuccess) return (int)status;
    NSData *data = CFBridgingRelease(result);
    char *copy = malloc(data.length + 1);
    if (!copy) return errSecAllocate;
    memcpy(copy, data.bytes, data.length);
    copy[data.length] = '\0';
    *value = copy;
    return errSecSuccess;
}

int bbf_keychain_write(const char *value) {
    if (!value) return errSecParam;
    NSData *data = [NSData dataWithBytes:value length:strlen(value)];
    NSDictionary *query = chatKeychainQuery();
    NSDictionary *update = @{ (__bridge id)kSecValueData: data };
    OSStatus status = SecItemUpdate((__bridge CFDictionaryRef)query, (__bridge CFDictionaryRef)update);
    if (status != errSecItemNotFound) return (int)status;
    NSMutableDictionary *item = [query mutableCopy];
    item[(__bridge id)kSecValueData] = data;
    status = SecItemAdd((__bridge CFDictionaryRef)item, NULL);
    // Another process may have created the item between update and add.
    if (status == errSecDuplicateItem) status = SecItemUpdate((__bridge CFDictionaryRef)query, (__bridge CFDictionaryRef)update);
    return (int)status;
}

int bbf_keychain_delete(void) {
    OSStatus status = SecItemDelete((__bridge CFDictionaryRef)chatKeychainQuery());
    return status == errSecItemNotFound ? errSecSuccess : (int)status;
}

void bbf_keychain_free(char *value) { free(value); }

static NSWindow *windowFor(void *handle) {
    id object = (__bridge id)handle;
    if ([object isKindOfClass:[NSWindow class]]) return object;
    if ([object isKindOfClass:[NSView class]]) return [(NSView *)object window];
    return nil;
}

int bbf_pointer(void *handle, double *x, double *y) {
    NSWindow *window = windowFor(handle);
    if (!window || !window.contentView) return 0;
    NSView *view = window.contentView;
    NSPoint point = [view convertPoint:window.mouseLocationOutsideOfEventStream fromView:nil];
    *x = point.x - view.bounds.origin.x;
    *y = view.isFlipped ? point.y - view.bounds.origin.y : NSMaxY(view.bounds) - point.y;
    return 1;
}

void bbf_clickthrough(void *handle, int enabled) {
    NSWindow *window = windowFor(handle);
    window.ignoresMouseEvents = enabled != 0;
    window.hasShadow = NO;
}

int bbf_application_active(void) { return NSApp.isActive ? 1 : 0; }
