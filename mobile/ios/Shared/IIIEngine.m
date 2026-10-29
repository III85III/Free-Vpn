#import "IIIEngine.h"
@import III85IIICore;
@implementation IIIEngine
+ (BOOL)start:(NSString *)directory on:(BOOL)on error:(NSError **)error { return AppleengineStartEngine(directory,on,error); }
+ (void)stop { AppleengineStopEngine(); }
+ (BOOL)running { return AppleengineEngineRunning(); }
+ (BOOL)connectionOn { return AppleengineConnectionOn(); }
+ (void)connect { AppleengineConnect(); }
+ (void)setOn:(BOOL)on { AppleengineSetOn(on); }
+ (NSString *)request:(NSString *)method path:(NSString *)path body:(NSString *)body error:(NSError **)error { return AppleengineAPIRequest(method,path,body,error); }
+ (BOOL)startPackets:(NSError **)error { return AppleengineStartPackets(error); }
+ (void)stopPackets { AppleengineStopPackets(); }
+ (NSData *)readPacket { return AppleengineReadPacket(); }
+ (BOOL)writePacket:(NSData *)packet error:(NSError **)error { return AppleengineWritePacket(packet,error); }
@end
