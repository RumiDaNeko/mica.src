# Mica

Browser desktop Windows lấy cảm hứng từ Arc, với giao diện WPF native và engine Microsoft Edge WebView2. Có **Spotlight search** để tìm tab đang mở ở mọi Space, bookmark, lịch sử, nhập URL hoặc tìm trên Google.

## Cài đặt

Chạy `installer-output/Mica-0.12.0-Setup.exe`. Installer giao diện Mica, kèm engine, cài cho tài khoản hiện tại, tạo shortcut và đăng ký Windows. Gỡ từ Settings → Apps; giữ nguyên dữ liệu duyệt web. Chưa ký số nên Windows có thể hiện Unknown publisher.

## Chạy

Mở `run.cmd` hoặc bản cập nhật `release-v0.12.0/Mica.exe`. Giữ các DLL cạnh file EXE. Nếu Mica đang mở, đóng cửa sổ hiện tại trước khi mở bản mới. Bản portable 0.9 yêu cầu Windows x64 và .NET Framework 4.8; không cần cài Edge/WebView2 riêng. Giải nén toàn bộ ZIP, giữ thư mục Runtime cạnh EXE. Bản build phát triển không kèm Runtime vẫn cần WebView2 cài trên máy.

## Cập nhật 0.11.3

Menu Space và menu trình duyệt bấm một lần mở, bấm lại đóng, không chạy lại animation mở. Áp dụng cả sidebar mở rộng và thu gọn.

## Cập nhật 0.11.2

Nền tab chọn phủ cả hàng, không phóng lệch khi hover. Giao diện WPF render bằng CPU để giảm lỗi mất khung liên quan GPU; trang web/video vẫn dùng GPU. Lỗi xuất hiện sau thời gian dài chưa tái hiện được; cần theo dõi thực tế.

## Cập nhật 0.11.1

- Ctrl+F thu gọn 430×90, neo góc dưới phải vùng nội dung và đi theo cửa sổ. Sidebar thu gọn dùng chung trục giữa cho nút và favicon.
- Ambient tích hợp dùng đúng **Ambient Light for YouTube** (`paponcgjfojgemddooebbgniglhkajkj`), thay hiệu ứng tự viết. Bật trong menu để tự tải/cài bản chính thức, tắt để vô hiệu hóa extension. Gói kiểm tra: 2.38.17.
- Trên Chrome Web Store, bấm Add to Chrome/Edge khi nút khả dụng, hoặc **Add to Mica** ở góc dưới phải. Mica tự tải, kiểm tra CRX và cài; không yêu cầu lưu/chọn file hoặc xác nhận lần hai. Luồng paste link/review vẫn có cho người muốn xem quyền trước.
- Popup quản lý extension bỏ thanh tiêu đề Windows, bo góc và theo theme.
- Khả năng tương thích extension vẫn phụ thuộc WebView2. Đã kiểm tra cài và bật/tắt Ambient thật; chưa xác nhận hiệu ứng trên mọi video/DRM YouTube.

## Cập nhật 0.11

- Ctrl+F: tìm trong trang với số kết quả, tiếp/trước, phân biệt hoa thường và nguyên từ.
- Ctrl+P: giao diện in theo theme; chọn máy in/PDF, phạm vi trang, bản sao, hướng giấy, nền và header/footer. Chưa có preview phân trang riêng.
- F12 / Ctrl+Shift+I: Console, DOM source, Network của Mica. Full engine tools mở bộ debugger đầy đủ.
- Shift+Esc: task manager có tab, RAM/CPU theo tiến trình, rest/close tab, bảo vệ tab ghim.
- Menu → YouTube ambient: hiệu ứng ánh sáng từ video, tùy chọn lưu cục bộ, có tắt/dọn và tôn trọng reduced motion.
- Cast: engine hiện tại không hỗ trợ Cast domain; có nút mở Windows Cast (Win+K) cho display mirroring/Miracast. Chưa có Chromecast/tab casting hoạt động trong bản này; chưa thử receiver thật.
- Tiến độ, kết quả kiểm tra và hướng dẫn tiếp tục cho Claude: [CONTINUE-HERE.md](CONTINUE-HERE.md).

## Cập nhật 0.10.1

- Đăng nhập Discord xong tự tải mật khẩu và snapshot đã đồng bộ; không cần bấm Pull. Snapshot chỉ áp dụng khi chọn Restore để giữ các tab đang mở.
- Giữ bản sao vault cũ đã mã hóa trước khi thay dữ liệu. Server trống hoặc tải thất bại không xóa dữ liệu cục bộ. Nếu mật khẩu cloud khác, popup yêu cầu mật khẩu đó; Hello cũ được gỡ khi đổi master password.

## Cập nhật 0.10

- Passwords mở popup mở khóa theo theme ngay khi vào; bỏ thẻ master password khỏi trang chính. Đóng popup sẽ đóng trang vault.
- Có thể bật Windows Hello bằng mật khẩu chính và xác thực Hello lần đầu; những lần sau dùng PIN/vân tay/khuôn mặt theo cấu hình Windows. Có phương án mật khẩu chính và Forget Windows Hello.
- Dữ liệu mở khóa chỉ lưu trên máy qua DPAPI, không đồng bộ. Đây là kiểm tra Hello do ứng dụng thực thi, không phải khóa mã hóa gắn TPM. Xem [Windows Hello](docs/windows-hello.md).
- 10 kiểm tra vault đã qua, gồm các luồng Hello dùng verifier thử nghiệm. Máy thử báo Hello không khả dụng; chưa xác thực PIN/sinh trắc học thật.

## Cập nhật 0.9.2

- Cửa sổ ẩn danh theo theme của cửa sổ thường, đồng bộ khi đổi sáng/tối. Chỉ chia sẻ lựa chọn theme; cookie/lịch sử vẫn cách ly và không ghi session ẩn danh. Tám kiểm tra privacy/Home/window đã qua.

## Cập nhật 0.9.1

- Căn giữa nút menu và các icon trên sidebar thu gọn; giữ bố cục sidebar mở rộng. 39 kiểm tra native đều qua.

## Cập nhật 0.9

- Đóng gói WebView2 Fixed Version 156.0.4314.42 x64 cùng ứng dụng, tự chọn engine đi kèm. Không cần cài Edge hay WebView2 riêng; đây vẫn là engine WebView2.
- Đã xác minh đường dẫn tiến trình engine nằm trong Runtime của gói, tải trang/JavaScript, smoke, extension và ẩn danh đều qua.
- Engine cố định không tự cập nhật; bản Mica mới phải mang theo các bản vá engine. Hướng dẫn: [portable-runtime.md](docs/portable-runtime.md). Tạo gói bằng package-portable.ps1.

## Cập nhật 0.8.4

- Chữ Mica. dùng Caveat, nhúng trong ứng dụng để hoạt động offline. Logo M. cùng font trên nền tím bo góc, gắn vào EXE và cửa sổ/taskbar. Font và giấy phép nằm trong src/Assets/Brand; bản phát hành kèm Caveat-OFL.txt.

## Cập nhật 0.8.3

- Thiết kế lại Passwords & sync: cửa sổ, thẻ và ô nhập bo góc; thêm icon rõ nghĩa; chia Passwords và Sync & account. Danh sách mật khẩu cạnh form chỉnh sửa, mật khẩu vẫn che ký tự.
- Giữ nguyên mã hóa, khóa khi đóng, kiểm tra origin khi điền và xác nhận thay dữ liệu. Năm kiểm tra vault/native sync đều qua trên bản mới.

## Cập nhật 0.8.2

- Ctrl+W bỏ qua tab đã ghim, kể cả khi chỉ còn một tab. Tab thường và tab đã bỏ ghim vẫn đóng bình thường. Cập nhật mô tả phím tắt và hint trong menu chuột phải.

## Cập nhật 0.8.1

- Bỏ hộp thoại Windows mặc định cho Keyboard shortcuts và hai xác nhận thay dữ liệu vault. Dùng giao diện Mica sáng/tối, nhóm phím tắt với keycap, nút đóng và thanh cuộn cùng theme.
- Esc/Done đóng bảng phím tắt; chỉ mở một bảng mỗi cửa sổ. Xác nhận thay dữ liệu giữ Cancel làm lựa chọn mặc định.

## Cập nhật 0.8

- Thiết kế lại Extensions với theme sáng/tối, icon thật, tìm kiếm, lọc All/Enabled/Disabled, bật/tắt và gỡ. Microsoft Clipboard Extension và Microsoft Edge PDF Viewer được ẩn khỏi danh sách; vẫn giữ hoạt động trong engine.
- **Menu → Extensions → dán link Chrome Web Store → Review extension → Add extension**. Browse store mở danh mục Store; khi mở Extensions từ trang chi tiết extension, link được điền sẵn. Nút Add to Chrome trên website chưa tích hợp.
- Tải gói từ endpoint Google, kiểm tra chữ ký CRX3/ID và giải nén có giới hạn trước khi hiển thị quyền. Chỉ cài sau khi xác nhận; Cancel dọn gói tạm. Gói được giữ trong profile nên không cần tự tải/giữ thư mục nguồn như Load unpacked.
- Gỡ hoặc thay phiên bản dọn thư mục gói Store cũ; không xóa thư mục extension unpacked của người dùng.
- Vẫn dùng WebView2: không bảo đảm mọi extension/API hoạt động, chưa có toolbar popup hoặc tự cập nhật. Dán lại link và xác nhận để thay phiên bản. Gói chỉ có chữ ký developer ECDSA chưa hỗ trợ.
- Đã kiểm tra Dark Reader 4.9.133 từ Store thật, cài đúng ID và thực thi stylesheet trên trang thử; kiểm tra tìm kiếm/lọc/bật/tắt/gỡ và giữ nguyên component hệ thống. Chi tiết: [release notes](docs/release-0.8.md).

## Cập nhật 0.7

- **Your Everyday** lấy tối đa bốn website từ lịch sử thật, xếp theo tổng số lượt truy cập từng origin, rồi lần dùng gần nhất. Không có lịch sử thì hiện trạng thái trống. Favicon lấy từ website/cache; các brand đã biết có ảnh dự phòng nhúng sẵn.
- **Jump List** khi chuột phải icon Mica trên taskbar: Top Sites, Recently closed, New window và New private window. Cần mở bản mới từ thư mục cố định rồi ghim lại icon nếu Windows còn ghim bản cũ. Windows có thể ẩn các nhóm lịch sử theo cài đặt quyền riêng tư. Mục người dùng đã bỏ khỏi Jump List không tự xuất hiện lại.
- **Ctrl+N** mở cửa sổ thường mới; **Ctrl+Shift+N** mở cửa sổ ẩn danh với nhãn PRIVATE luôn hiện trên thanh địa chỉ. Các cửa sổ thường chia sẻ cookies/thư viện; mỗi cửa sổ ẩn danh dùng profile InPrivate riêng, không ghi lịch sử, session, favicon lên đĩa, không thêm Top Sites/Recently closed và không mở vault/extensions. File chủ động tải xuống vẫn được giữ. Profile engine tạm được dọn khi đóng, có thử lại ở lần khởi động sau nếu engine còn khóa file hoặc ứng dụng bị crash.
- Khôi phục các style native caption/resize/maximize để Windows/DWM xử lý chuyển động cửa sổ. Giữ giới hạn kích thước và vị trí footer. Hiệu ứng phụ thuộc cài đặt animation của Windows; chưa xác nhận giống Edge từng khung hình.
- **Menu → Passwords & sync**: kho mật khẩu thủ công, lưu/sửa/xóa, copy hoặc điền vào website HTTPS đúng origin khi người dùng bấm. Không tự thu thập mật khẩu và không tự submit form. Đóng cửa sổ vault để khóa lại.
- Vault dùng mật khẩu chính riêng (tối thiểu 12 ký tự khi tạo), PBKDF2-SHA256 600.000 vòng, AES-256-CBC + HMAC-SHA256 theo Encrypt-then-MAC. Discord chỉ xác thực tài khoản. Server nhận bản mã; token đăng nhập trên máy được bảo vệ bằng Windows DPAPI. Quên mật khẩu chính không có khôi phục từ Discord.
- Đồng bộ **thủ công** qua `https://the-api.lessnoise.online`: Sign in → Unlock/create → Pull remote → Push local. Capture browser snapshot lưu tabs/bookmarks/history/theme/Space trong vault; Restore là thao tác riêng có xác nhận. Pull có thể thay thế vault local sau xác nhận; không có merge tự động. Xung đột phiên bản trả lỗi, không ghi đè âm thầm.
- **Chưa lưu/đồng bộ passkey riêng**: có nút mở cài đặt passkey Windows. Snapshot không bao gồm cookies/phiên đăng nhập website, file tải, download metadata hay package extension. Bản này vẫn dùng WebView2.
- Server Node.js 24 + SQLite, cấu hình Docker/nginx và hướng dẫn deploy: [server/README.md](server/README.md). API và Discord chưa deploy/cấu hình thật; đã kiểm tra OAuth giả lập và roundtrip mã hóa từ ứng dụng tới Node.js cục bộ.

## Cập nhật 0.6.1

- Home dùng favicon thật của GitHub, YouTube, Wikipedia và Gmail, nhúng sẵn để hiển thị offline.
- Ghi nhớ yêu cầu, kiến trúc, kiểm thử và hướng đánh giá engine mới trong `memory.md`. Bản này vẫn dùng WebView2.

## Cập nhật 0.6

- Menu rút gọn còn History, Downloads, Bookmarks, Extensions, Rest background tabs và Keyboard shortcuts; bỏ các lệnh trùng với sidebar/Spotlight.
- **History — Ctrl+H**: trang native `mica://history`, tìm theo tiêu đề/URL, nhóm theo ngày, lọc Today/Past 7 days, mở lại trang, xóa từng mục hoặc xác nhận xóa toàn bộ lịch sử. Dùng lại lịch sử Mica đang có.
- **Downloads — Ctrl+J**: trang native `mica://downloads`, chọn nơi lưu bằng hộp thoại Windows, hiện tiến độ/byte thật, Pause/Resume/Cancel, mở file, mở thư mục và lọc Active/Completed. Theo dõi các lượt tải bắt đầu từ bản 0.6; không nhập danh sách tải cũ từ engine.
- Tải đang chạy hoặc tạm dừng giữ engine sống khi đóng tab nguồn hay cho tab nền ngủ. Trang đã đóng không được mở popup hoặc khởi tạo lượt tải mới. Engine được giải phóng khi lượt tải cuối kết thúc.
- Khi đóng ứng dụng, lượt tải chưa xong bị dừng. Lần mở sau hiển thị Interrupted và cho mở lại địa chỉ tải; chưa tiếp tục tải xuyên phiên. Resume trong cùng phiên phụ thuộc khả năng của engine/server.
- Xóa bản ghi không xóa file. Giữ tối đa 200 bản ghi tải đã kết thúc, với ngân sách metadata khoảng 1 MiB (URL dài có thể làm số mục thấp hơn); không loại bỏ lượt tải còn sống. Timer cập nhật dừng khi không có thay đổi.
- Hai trang hỗ trợ theme sáng/tối, sidebar thu gọn và cửa sổ nhỏ. Là giao diện WPF native, không tạo thêm engine web để hiển thị thư viện.

## Cập nhật 0.5

- Sửa cụm Menu/theme bị khuất khi maximize: dùng vùng làm việc của màn hình hiện tại, chừa taskbar và giữ giới hạn kích thước tối thiểu của Windows/WPF.
- Phần đáy sidebar luôn có chỗ riêng; tab thường và tab ghim cùng nằm trong vùng cuộn, không đẩy Menu/theme ra ngoài khi có nhiều tab.
- Icon refresh xoay trong lúc tab hiện tại tải trang, kết thúc nhẹ khi tải xong/lỗi; đổi sang tab không tải sẽ dừng. Không giữ vòng animation chạy khi nghỉ; tôn trọng tùy chọn animation Windows.
- Menu chuột phải trên tab và website được thiết kế lại: góc bo, icon, phím tắt, nhóm lệnh, submenu và màu sáng/tối. Menu trang web giữ các lệnh gốc của engine. Menu tab có ghim, chuyển Space, tải lại và đóng tab.
- Kiểm thử trực tiếp vị trí cụm đáy sau maximize, 30 tab ghim, giới hạn resize, vòng đời refresh và lệnh reload thật từ menu trang web; đóng tab khi menu đang mở giải phóng yêu cầu native trước khi hủy tab.

## Cập nhật 0.4

- Bỏ cụm GH/YouTube/Gmail trong sidebar và hai nút Home/Work cũ. Giữ đúng một nút thu gọn ở đầu sidebar.
- Dải thu gọn có kính lúp Spotlight, chọn Space, favicon từng tab, thêm tab, theme và menu. Cuộn chuột để xem nhiều tab; chuột phải trên icon để ghim, chuyển Space hoặc đóng tab.
- Favicon lấy từ website qua engine, lưu thumbnail tối đa 32 px, cache tối đa 256 icon; vẫn hiện khi tab ngủ hoặc mở lại ứng dụng. Site chưa có icon dùng chữ cái đầu.
- Spaces là dropdown hiển thị lựa chọn hiện tại và số tab. Menu mới có tên lệnh, nhóm chức năng và gợi ý phím tắt.
- Animation cho sidebar, Spotlight, dropdown, hover/nhấn, tab mới và trang chủ. Resize dùng xử lý native Windows, viền phản hồi nhẹ và typography/hình trang chủ chuyển kích thước; maximize/minimize theo Windows. Không thêm animation vào nội dung website. Tôn trọng tùy chọn animation của Windows.
- **Menu → Extensions → Load unpacked…**: chọn `manifest.json` trong extension đã giải nén. Có bật, tắt, gỡ. Hai Space dùng chung extension. Giữ nguyên thư mục nguồn; ứng dụng không sao chép package.
- **Chưa hỗ trợ đầy đủ Chrome extension**: không cài trực tiếp Chrome Web Store/CRX, chưa có popup/toolbar extension như Chrome; API Chrome riêng có thể không hoạt động. Đã kiểm tra extension MV3 mẫu chạy content script thật qua cài → tắt → bật → gỡ. Extension tích hợp của WebView2 hiển thị chỉ đọc.

## Cập nhật 0.3

- Chỉ giữ một nút thu gọn ở đầu sidebar, bỏ nút trùng trên toolbar. Khi thu gọn còn dải 64 px chứa nút để mở lại bằng chuột.
- Sidebar trượt 220 ms, nội dung mờ dần; Spotlight fade và trượt nhẹ khi mở/đóng. Tôn trọng cài đặt Windows tắt animation trong vùng nội dung.
- Bấm liên tục sẽ đổi hướng từ vị trí hiện tại, không chờ hiệu ứng trước hoàn tất. Không có timer animation chạy thường trực.
- Đã kiểm tra animation ở giữa chuyển động, đổi hướng nhanh, mở lại Spotlight trong lúc đang đóng, mở lại sidebar từ dải nhỏ và các chức năng browser hiện có.

## Cập nhật 0.2

- Sửa báo lỗi `ConnectionAborted` sai khi website đã trả HTTP 2xx rồi chủ động dừng tải: giữ nội dung trang thay vì che bằng màn hình lỗi. Lỗi mạng thật và chứng chỉ vẫn hiển thị lỗi.
- Bỏ qua kết quả tải cũ khi lượt điều hướng mới đã bắt đầu; không đưa tab đang tải vào chế độ ngủ.
- Bản 0.2 dùng nút sidebar trên toolbar; bản 0.3 chuyển nút duy nhất về sidebar theo yêu cầu. Phím `Ctrl+Shift+B` vẫn hoạt động.
- Thanh trên có vùng kéo native Windows: kéo vùng trống để di chuyển, nhấp đúp để phóng to/khôi phục. Nút cửa sổ thu nhỏ còn 28×26, biểu tượng 10.
- 29 kiểm thử logic; kiểm thử native cho sidebar, hit-test vùng kéo/nút cửa sổ, trang HTTP 200 tự gọi `window.stop()` và điều hướng liên tiếp. Đã kiểm tra example.com, GitHub và trang đăng nhập Gmail bằng profile riêng; chưa kiểm tra Gmail sau đăng nhập.

## Tính năng

- Sidebar tab dọc, ghim tab bằng menu chuột phải, đóng tab, chuyển tab.
- Personal / Work Spaces; chuyển Space và chuyển tab qua Spotlight.
- Spotlight dùng ↑/↓ chọn, Enter mở, Esc đóng; tab được ưu tiên trước bookmark và lịch sử.
- Back, Forward, Reload, bookmark, lịch sử tìm kiếm, lưu phiên và theme sáng/tối.
- Tab nền được giải phóng sau 2 phút không hoạt động. Tab phát âm thanh được giữ lại. Menu có lệnh cho tab nền ngủ ngay.
- Hiển thị tổng working set của Mica và các tiến trình WebView2 liên quan, không hiển thị số RAM giả theo tab.

| Phím | Thao tác |
|---|---|
| Ctrl+K | Spotlight |
| Ctrl+L | Sửa địa chỉ tab hiện tại |
| Ctrl+T / Ctrl+W | Mở / đóng tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Tab kế / trước |
| Ctrl+D | Thêm / bỏ bookmark |
| Ctrl+H / Ctrl+J | Lịch sử / tải xuống |
| Ctrl+N / Ctrl+Shift+N | Cửa sổ thường / ẩn danh |
| Ctrl+R hoặc F5 | Tải lại |
| Alt+Left / Alt+Right | Back / Forward |
| Ctrl+Shift+B | Ẩn / hiện sidebar |

## RAM và giới hạn

**Không đạt 1,1 MB cho mỗi tab web đang chạy.** Engine hiện đại có nhiều tiến trình, trang web, GPU và cache. Trong lần kiểm thử đầu: khoảng 99 MB ở trang chủ native, 436 MB khi mở example.com (6 tiến trình), và 126 MB sau khi giải phóng tab web. Đây là ảnh chụp tại thời điểm đo, không phải benchmark chuẩn hay cam kết. Kết quả kiểm thử mới nhất ở `artifacts/smoke-result.txt`. Tổng working set có thể đếm trùng một số trang nhớ dùng chung.

Tab ngủ giữ địa chỉ, tiêu đề, Space và favicon đã cache. Khi mở lại, trang tải lại; form chưa gửi, vị trí cuộn và lịch sử Back/Forward riêng của tab ngủ không được bảo toàn. Ghim tab không ngăn tab ngủ. Phiên khởi động lại cũng khôi phục bằng URL. Hai Space dùng chung cookies và đăng nhập; không phải hai hồ sơ riêng tư.

Chưa có đồng bộ tài khoản, split view, đặt browser mặc định hay bộ cài có chữ ký. Giao diện dùng tiếng Anh. OAuth, DRM, camera/microphone và các website phức tạp chưa được kiểm thử toàn diện. WebView2 xử lý hộp thoại quyền; Mica có giao diện quản lý tải file riêng. Ứng dụng không tự cấp quyền, không tắt kiểm tra chứng chỉ và không cung cấp cầu nối native cho nội dung website.

## Dữ liệu

Dữ liệu thật nằm tại `%LOCALAPPDATA%\MicaBrowser`: `session.json` chứa tab/bookmark/lịch sử, `downloads.json` chứa thông tin tải xuống, thư mục `WebView` chứa cookies và dữ liệu website. File tải được lưu ở nơi bạn chọn. Thư mục `Favicons` chứa thumbnail icon; extension được lưu trong profile WebView2 và tham chiếu thư mục nguồn đã chọn. Lịch sử ứng dụng giới hạn 500 địa chỉ. History → Clear history chỉ xóa danh sách này, giữ cookies và dữ liệu website. Không có telemetry do Mica thêm vào. Truy vấn Google chỉ được gửi khi chọn kết quả tìm web và nhấn Enter.

## Build và kiểm thử

```powershell
.\build.ps1
.\build.ps1 -OutputDirectory release
.\build.ps1 -OutputDirectory release-v0.2
.\test.ps1
```

Build dùng compiler .NET Framework có sẵn trên Windows, tải gói Microsoft.Web.WebView2 **1.0.4258.31** vào `.deps` nếu thiếu; không cài SDK toàn máy. Source được nhúng cùng giao diện XAML vào EXE; các DLL đi kèm nằm trong `dist`.

Kiểm thử ứng dụng native, với profile riêng biệt:

```powershell
$check = Start-Process .\dist\Mica.exe -ArgumentList '--smoke-test' -PassThru
$check.WaitForExit()
Get-Content .\artifacts\smoke-result.txt
```

Chế độ này mở example.com để kiểm tra engine, nhập địa chỉ rồi Back, phục hồi vùng web sau trạng thái lỗi, bookmark, Spotlight xuyên Space, giải phóng/phục hồi tab ngủ, lưu phiên và theme. Nó xuất PNG trang chủ, Spotlight và dark mode để kiểm tra trực quan; profile kiểm thử ở `artifacts/smoke-*`, không dùng dữ liệu thật. Phím tắt được kiểm tra qua handler của ứng dụng; thao tác phím vật lý từ OS chưa được tự động hóa.

Thêm cờ `--regression-test` thay cho `--smoke-test` để kiểm tra sidebar nhiều tab, Spaces/menu, favicon thật, animation bị ngắt, double-click Spotlight, native resize/maximize và lỗi điều hướng bằng server loopback; `--extensions-test` kiểm tra extension MV3 với profile riêng; `--sites-test` kiểm tra ba website nêu trên. Các báo cáo nằm ở `artifacts/regression-result.txt` và `artifacts/sites-result.txt`.

## Source

- `src/BrowserLibrary.cs`, `Library.xaml`: trang History và Downloads native.
- `src/BrowserDownloads.cs`, `DownloadModel.cs`: vòng đời tải thật và lưu bản ghi; `tests/LibraryRegression.cs`, `DownloadRegression.cs` kiểm tra bằng `--library-test` và `--downloads-test`.

- `src/Core.cs`: dữ liệu tab, URL, xếp kết quả Spotlight, chính sách ngủ và lưu JSON.
- `src/BrowserApp.cs`: vòng đời ứng dụng, WebView2, bàn phím, điều hướng và smoke test.
- `src/Shell.xaml`: giao diện native.
- `src/BrowserMenus.cs`, `BrowserMotion.cs`: menu, Spaces và animation.
- `src/BrowserContextMenus.cs`, `ContextMenus.xaml`: menu chuột phải tab/website.
- `src/BrowserWindow.cs`: giới hạn maximize theo vùng làm việc màn hình.
- `src/BrowserIcons.cs`, `BrowserExtensions.cs`: favicon và extension manager.
- `tests/CoreTests.cs`: kiểm thử hành vi không phụ thuộc giao diện.


