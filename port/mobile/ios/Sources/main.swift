// The iPhone app shell (port/mobile/ios/README.md): a full-screen web view that
// runs the phone build packed inside the app, so the game opens with no PC and
// no browser. The game files are added on the PC by
// port/mobile/tools/pack_ipa.py; this shell holds none of them.
import UIKit

UIApplicationMain(CommandLine.argc, CommandLine.unsafeArgv, nil, NSStringFromClass(AppDelegate.self))
