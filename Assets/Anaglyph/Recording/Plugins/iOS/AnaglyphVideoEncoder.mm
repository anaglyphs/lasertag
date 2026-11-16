// Writes RGBA frames and float audio into an HEVC + AAC .mp4 with AVAssetWriter, then adds the file to Photos.
#import <AVFoundation/AVFoundation.h>
#import <Accelerate/Accelerate.h>
#import <Photos/Photos.h>
#include <atomic>

// matches Anaglyph.Recording.VideoEncoderState
enum { StateIdle, StateRecording, StateFinishing, StateSaved, StateFailed };

static const int32_t TicksPerSecond = 90000;
// bits per pixel per frame; 4K at 30 fps comes to about 45 Mbit/s
static const double VideoBitsPerPixel = 0.18;
// AAC refuses more bits than a low sample rate can carry; two per sample suits every rate
static const int AudioBitsPerSamplePerChannel = 2;

static AVAssetWriter* writer;
static AVAssetWriterInput* videoInput;
static AVAssetWriterInput* audioInput;
static AVAssetWriterInputPixelBufferAdaptor* pixelBuffers;
static CMAudioFormatDescriptionRef audioFormat;
static int frameWidth, frameHeight, audioChannels;
static CMTime lastVideoTime;
static std::atomic<int> state(StateIdle);
static NSString* errorText = @"";

static void Fail(NSString* reason)
{
	NSLog(@"[AnaglyphVideoEncoder] %@", reason);
	@synchronized ([AVAssetWriter class]) { errorText = [reason copy]; }
	state = StateFailed;
}

static bool MayAddToPhotos(PHAuthorizationStatus status)
{
	return status == PHAuthorizationStatusAuthorized || status == PHAuthorizationStatusLimited;
}

// 0 not asked yet, 1 allowed, 2 refused
extern "C" int AnaglyphVideoEncoderSaveAccess()
{
	PHAuthorizationStatus status = [PHPhotoLibrary authorizationStatusForAccessLevel:PHAccessLevelAddOnly];
	return status == PHAuthorizationStatusNotDetermined ? 0 : MayAddToPhotos(status) ? 1 : 2;
}

// asks to add to Photos only, never to read the library; shows the system prompt the first time
extern "C" void AnaglyphVideoEncoderRequestSaveAccess()
{
	[PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelAddOnly handler:^(PHAuthorizationStatus status) { }];
}

static void SaveToPhotos(NSURL* file)
{
	[PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelAddOnly handler:^(PHAuthorizationStatus status)
	{
		if (!MayAddToPhotos(status))
		{
			[[NSFileManager defaultManager] removeItemAtURL:file error:nil];
			Fail(@"Lasertag is not allowed to add videos to Photos");
			return;
		}

		[[PHPhotoLibrary sharedPhotoLibrary] performChanges:^
		{
			[PHAssetChangeRequest creationRequestForAssetFromVideoAtFileURL:file];
		}
		completionHandler:^(BOOL saved, NSError* error)
		{
			[[NSFileManager defaultManager] removeItemAtURL:file error:nil];
			if (saved) state = StateSaved;
			else Fail(error.localizedDescription ?: @"Photos refused the video");
		}];
	}];
}

extern "C" bool AnaglyphVideoEncoderBegin(const char* path, int width, int height, int framesPerSecond, int sampleRate, int channels)
{
	if (state == StateRecording || state == StateFinishing) return false;

	NSURL* file = [NSURL fileURLWithPath:[NSString stringWithUTF8String:path]];
	[[NSFileManager defaultManager] removeItemAtURL:file error:nil];

	NSError* error = nil;
	writer = [AVAssetWriter assetWriterWithURL:file fileType:AVFileTypeMPEG4 error:&error];
	if (writer == nil)
	{
		Fail(error.localizedDescription ?: @"Could not create the video file");
		return false;
	}

	frameWidth = width;
	frameHeight = height;
	audioChannels = channels;

	NSDictionary* videoSettings = @{
		AVVideoCodecKey: AVVideoCodecTypeHEVC,
		AVVideoWidthKey: @(width),
		AVVideoHeightKey: @(height),
		AVVideoCompressionPropertiesKey: @{
			AVVideoAverageBitRateKey: @((long)(width * (double)height * framesPerSecond * VideoBitsPerPixel)),
			AVVideoExpectedSourceFrameRateKey: @(framesPerSecond),
		},
	};
	videoInput = [AVAssetWriterInput assetWriterInputWithMediaType:AVMediaTypeVideo outputSettings:videoSettings];
	videoInput.expectsMediaDataInRealTime = YES;
	pixelBuffers = [AVAssetWriterInputPixelBufferAdaptor
		assetWriterInputPixelBufferAdaptorWithAssetWriterInput:videoInput
		sourcePixelBufferAttributes:@{
			(id)kCVPixelBufferPixelFormatTypeKey: @(kCVPixelFormatType_32BGRA),
			(id)kCVPixelBufferWidthKey: @(width),
			(id)kCVPixelBufferHeightKey: @(height),
		}];

	AudioChannelLayout layout = {};
	layout.mChannelLayoutTag = channels == 1 ? kAudioChannelLayoutTag_Mono : kAudioChannelLayoutTag_Stereo;
	NSDictionary* audioSettings = @{
		AVFormatIDKey: @(kAudioFormatMPEG4AAC),
		AVSampleRateKey: @(sampleRate),
		AVNumberOfChannelsKey: @(channels),
		AVEncoderBitRateKey: @(AudioBitsPerSamplePerChannel * sampleRate * channels),
		AVChannelLayoutKey: [NSData dataWithBytes:&layout length:sizeof(layout)],
	};
	audioInput = channels <= 2 ? [AVAssetWriterInput assetWriterInputWithMediaType:AVMediaTypeAudio outputSettings:audioSettings] : nil;
	audioInput.expectsMediaDataInRealTime = YES;

	AudioStreamBasicDescription samples = {};
	samples.mSampleRate = sampleRate;
	samples.mFormatID = kAudioFormatLinearPCM;
	samples.mFormatFlags = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked;
	samples.mChannelsPerFrame = channels;
	samples.mBitsPerChannel = 32;
	samples.mBytesPerFrame = 4 * channels;
	samples.mBytesPerPacket = samples.mBytesPerFrame;
	samples.mFramesPerPacket = 1;
	if (audioFormat != NULL) CFRelease(audioFormat);
	audioFormat = NULL;
	CMAudioFormatDescriptionCreate(kCFAllocatorDefault, &samples, 0, NULL, 0, NULL, NULL, &audioFormat);

	if (![writer canAddInput:videoInput])
	{
		Fail(@"This device cannot encode video at this size");
		return false;
	}
	[writer addInput:videoInput];
	if (audioInput != nil && [writer canAddInput:audioInput]) [writer addInput:audioInput];
	else audioInput = nil;

	if (![writer startWriting])
	{
		Fail(writer.error.localizedDescription ?: @"Could not start writing the video");
		return false;
	}
	[writer startSessionAtSourceTime:kCMTimeZero];
	lastVideoTime = kCMTimeInvalid;
	state = StateRecording;
	return true;
}

extern "C" void AnaglyphVideoEncoderAppendVideo(const void* rgbaBottomRowFirst, double seconds)
{
	if (state != StateRecording || !videoInput.readyForMoreMediaData) return;

	CMTime time = CMTimeMakeWithSeconds(seconds, TicksPerSecond);
	if (CMTIME_IS_VALID(lastVideoTime) && CMTimeCompare(time, lastVideoTime) <= 0) return;

	CVPixelBufferRef frame = NULL;
	if (pixelBuffers.pixelBufferPool == NULL ||
	    CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault, pixelBuffers.pixelBufferPool, &frame) != kCVReturnSuccess)
		return;

	CVPixelBufferLockBaseAddress(frame, 0);
	uint8_t* destination = (uint8_t*)CVPixelBufferGetBaseAddress(frame);
	size_t destinationRowBytes = CVPixelBufferGetBytesPerRow(frame);
	size_t sourceRowBytes = (size_t)frameWidth * 4;
	const uint8_t redAndBlueSwapped[4] = { 2, 1, 0, 3 };
	for (int row = 0; row < frameHeight; row++)
	{
		vImage_Buffer from = { (void*)((const uint8_t*)rgbaBottomRowFirst + (size_t)(frameHeight - 1 - row) * sourceRowBytes), 1, (vImagePixelCount)frameWidth, sourceRowBytes };
		vImage_Buffer to = { destination + (size_t)row * destinationRowBytes, 1, (vImagePixelCount)frameWidth, destinationRowBytes };
		vImagePermuteChannels_ARGB8888(&from, &to, redAndBlueSwapped, kvImageNoFlags);
	}
	CVPixelBufferUnlockBaseAddress(frame, 0);

	if ([pixelBuffers appendPixelBuffer:frame withPresentationTime:time]) lastVideoTime = time;
	else Fail(writer.error.localizedDescription ?: @"Could not write a video frame");
	CVPixelBufferRelease(frame);
}

extern "C" void AnaglyphVideoEncoderAppendAudio(const void* interleaved, int frameCount, double seconds)
{
	if (state != StateRecording || audioInput == nil || audioFormat == NULL || frameCount <= 0 || !audioInput.readyForMoreMediaData) return;

	size_t byteCount = (size_t)frameCount * 4 * audioChannels;
	CMBlockBufferRef block = NULL;
	if (CMBlockBufferCreateWithMemoryBlock(kCFAllocatorDefault, NULL, byteCount, kCFAllocatorDefault, NULL, 0, byteCount,
	                                       kCMBlockBufferAssureMemoryNowFlag, &block) != kCMBlockBufferNoErr)
		return;
	CMBlockBufferReplaceDataBytes(interleaved, block, 0, byteCount);

	CMSampleBufferRef sampleBuffer = NULL;
	OSStatus created = CMAudioSampleBufferCreateReadyWithPacketDescriptions(kCFAllocatorDefault, block, audioFormat, frameCount,
		CMTimeMakeWithSeconds(seconds, TicksPerSecond), NULL, &sampleBuffer);
	CFRelease(block);
	if (created != noErr) return;

	if (![audioInput appendSampleBuffer:sampleBuffer])
		Fail(writer.error.localizedDescription ?: @"Could not write audio");
	CFRelease(sampleBuffer);
}

extern "C" void AnaglyphVideoEncoderFinish()
{
	if (writer == nil) return;
	bool failed = state == StateFailed;
	if (state != StateRecording && !failed) return;
	if (!failed) state = StateFinishing;

	AVAssetWriter* finishing = writer;
	writer = nil;
	if (finishing.status != AVAssetWriterStatusWriting)
	{
		[finishing cancelWriting];
		if (!failed) Fail(finishing.error.localizedDescription ?: @"The video was not written");
		return;
	}

	[videoInput markAsFinished];
	[audioInput markAsFinished];
	[finishing finishWritingWithCompletionHandler:^
	{
		if (failed) [[NSFileManager defaultManager] removeItemAtURL:finishing.outputURL error:nil];
		else if (finishing.status == AVAssetWriterStatusCompleted) SaveToPhotos(finishing.outputURL);
		else Fail(finishing.error.localizedDescription ?: @"The video was not written");
	}];
}

extern "C" int AnaglyphVideoEncoderState() { return state; }

extern "C" const char* AnaglyphVideoEncoderError()
{
	@synchronized ([AVAssetWriter class]) { return errorText.UTF8String; }
}
