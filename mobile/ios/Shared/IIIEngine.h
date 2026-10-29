#import <Foundation/Foundation.h>
NS_ASSUME_NONNULL_BEGIN
@interface IIIEngine : NSObject
+ (BOOL)start:(NSString *)directory on:(BOOL)on error:(NSError **)error NS_SWIFT_NOTHROW;
+ (void)stop;
+ (BOOL)running;
+ (BOOL)connectionOn;
+ (void)connect;
+ (void)setOn:(BOOL)on;
+ (nullable NSString *)request:(NSString *)method path:(NSString *)path body:(NSString *)body error:(NSError **)error NS_SWIFT_NOTHROW;
+ (BOOL)startPackets:(NSError **)error NS_SWIFT_NOTHROW;
+ (void)stopPackets;
+ (nullable NSData *)readPacket;
+ (BOOL)writePacket:(NSData *)packet error:(NSError **)error NS_SWIFT_NOTHROW;
@end
NS_ASSUME_NONNULL_END
