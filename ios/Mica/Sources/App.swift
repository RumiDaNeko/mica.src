import UIKit
import AVFoundation

@main
final class AppDelegate: UIResponder, UIApplicationDelegate {
    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?) -> Bool {
        // Audio and video keep playing with the screen off or in another app, as in any browser.
        try? AVAudioSession.sharedInstance().setCategory(.playback, mode: .default)
        Blocker.shared.load()
        DispatchQueue.global(qos: .utility).async { Reader.trimCache() }
        return true
    }

    func application(_ application: UIApplication, configurationForConnecting connectingSceneSession: UISceneSession, options: UIScene.ConnectionOptions) -> UISceneConfiguration {
        let config = UISceneConfiguration(name: nil, sessionRole: connectingSceneSession.role)
        config.delegateClass = SceneDelegate.self
        return config
    }

    func applicationDidReceiveMemoryWarning(_ application: UIApplication) {
        Browser.shared.trim()
    }
}

final class SceneDelegate: UIResponder, UIWindowSceneDelegate {
    var window: UIWindow?

    func scene(_ scene: UIScene, willConnectTo session: UISceneSession, options connectionOptions: UIScene.ConnectionOptions) {
        guard let scene = scene as? UIWindowScene else { return }
        let window = UIWindow(windowScene: scene)
        window.overrideUserInterfaceStyle = Prefs.shared.theme.style
        let main = MainViewController()
        window.rootViewController = main
        window.makeKeyAndVisible()
        self.window = window
        if let url = connectionOptions.urlContexts.first?.url { main.open(external: url) }
        if let activity = connectionOptions.userActivities.first, let url = activity.webpageURL { main.open(external: url) }
    }

    func scene(_ scene: UIScene, openURLContexts URLContexts: Set<UIOpenURLContext>) {
        guard let url = URLContexts.first?.url else { return }
        (window?.rootViewController as? MainViewController)?.open(external: url)
    }

    func scene(_ scene: UIScene, continue userActivity: NSUserActivity) {
        guard let url = userActivity.webpageURL else { return }
        (window?.rootViewController as? MainViewController)?.open(external: url)
    }

    func sceneDidEnterBackground(_ scene: UIScene) { Browser.shared.onBackground() }
    func sceneWillEnterForeground(_ scene: UIScene) { Browser.shared.onForeground(); Account.shared.onForeground() }
}
