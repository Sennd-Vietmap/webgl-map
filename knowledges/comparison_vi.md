# Cầu nối Lập trình Đồ họa: Từ WebGL đến OpenTK
**Phiên bản: 0.2.0**

## Kiến thức chuyên sâu cho Kỹ sư Đồ họa tương lai

Tài liệu này đóng vai trò cầu nối cho các lập trình viên chuyển đổi giữa WebGL (JavaScript) và OpenTK (.NET), đồng thời cung cấp kiến thức nền tảng quan trọng cho lập trình đồ họa hiện đại. Tại đây, chúng ta sẽ đi sâu vào lý do *tại sao* (why) chứ không chỉ đơn thuần là *làm thế nào* (how).

---

## 1. Quy trình Render (The Pipeline): Từ Code đến Điểm ảnh

Hiểu rõ quy trình (pipeline) là chìa khóa để debug. Dù là WebGL hay OpenTK, luồng xử lý là giống hệt nhau:

1.  **Xử lý Đỉnh (Vertex Processing - Vertex Shader)**:
    *   **Đầu vào**: Tọa độ thô từ VBO của bạn (ví dụ: Kinh độ/Vĩ độ hoặc X/Y đơn giản).
    *   **Thao tác**: Nhân với các Ma trận (Model $\rightarrow$ View $\rightarrow$ Projection) để biến đổi tọa độ thế giới thành **Clip Space** (Không gian cắt, phạm vi $[-1.0, 1.0]$).
    *   **Đầu ra**: `gl_Position` (Vị trí cuối cùng trên màn hình).

2.  **Rasterization (Chức năng cố định)**:
    *   GPU lấy hình tam giác được tạo bởi 3 đỉnh (trong clip-space) và tính toán xem nó bao phủ những pixel nào trên màn hình.
    *   Nó nội suy (interpolate) các giá trị "varyings" (như màu sắc, tọa độ texture/UV) trên bề mặt của pixel.

3.  **Xử lý Mảnh (Fragment Processing - Fragment Shader)**:
    *   **Đầu vào**: Dữ liệu đã được nội suy cho *một pixel cụ thể*.
    *   **Đầu ra**: Màu RGBA cuối cùng.

---

## 2. Bộ nhớ: CPU và GPU (Buffers)

Một trong những khái niệm khó nhất cho người mới bắt đầu là: **OpenGL có kiến trúc Client-Server**.
*   **CPU (Client)**: Code C# hoặc JS của bạn.
*   **GPU (Server)**: Card đồ họa rời hoặc tích hợp.

Bạn không thể đơn giản là "đọc" một mảng C# từ trong Shader. Bạn phải chuyển nó đi.

### "Vũ điệu" VAO/VBO
1.  **VBO (Vertex Buffer Object)**: Một khối bộ nhớ riêng biệt trên RAM của GPU. Bạn dùng lệnh `GL.BufferData` để copy từng byte từ CPU $\rightarrow$ GPU.
2.  **VAO (Vertex Array Object)**: Một đối tượng "Ghi nhớ trạng thái" (Save State). Nó ghi nhớ cấu hình về *cách thức* đọc dữ liệu từ VBO.
    *   *Nếu không có VAO*: Bạn phải cấu hình lại con trỏ (`GL.VertexAttribPointer`) ở mỗi frame (khung hình).
    *   *Với VAO*: Bạn cấu hình một lần lúc khởi tạo. Khi render, bạn chỉ cần bind VAO đó, và OpenGL tự nhớ: "Thuộc tính số 0 là 2 số float, bắt đầu từ byte số 0".

**Sự khác biệt**:
*   **WebGL**: Thường trừu tượng hóa bớt sự phức tạp của VAO (Extension OES_vertex_array_object trong WebGL 1, chuẩn trong WebGL 2).
*   **OpenTK**: Bạn thường phải quản lý thủ công. **Luôn luôn bind VAO trước khi vẽ.**

---

## 3. Toán học Ma trận (Matrix MVP)

Đây là nơi 90% các lỗi đồ họa xảy ra (như lỗi màn hình trắng của chúng ta).

### Khái niệm: Các phép biến đổi
$$ \text{Vị trí Clip} = \text{Projection} \times \text{View} \times \text{Model} \times \text{Vị trí Cục bộ} $$

1.  **Model Matrix**: Di chuyển vật thể từ gốc tọa độ $(0,0)$ của chính nó đến vị trí trong thế giới.
2.  **View Matrix**: Di chuyển *toàn bộ thế giới* sao cho camera nằm tại $(0,0)$ và nhìn về phía trước.
    *   *Mẹo*: Nếu camera di chuyển sang Phải $(+10)$, thì thế giới phải di chuyển sang Trái $(-10)$. Do đó, View Matrix là **Nghịch đảo** (Inverse) của Camera Matrix.
3.  **Projection Matrix**: Nén thế giới 3D vào trong chiếc hộp 2D phạm vi $[-1, 1]$ (Phối cảnh hoặc Trực giao).

### Khác biệt chí mạng: Bố cục dữ liệu (Layout)
Bộ nhớ máy tính là tuyến tính (mảng 1 chiều). Một ma trận 4x4 (16 số) được ánh xạ vào mảng này theo cách khác nhau.

| Hệ thống | Bố cục bộ nhớ | Toán Vector | Thứ tự lệnh (Code) |
|----------|---------------|-------------|---------------------|
| **WebGL (gl-matrix)** | **Column-Major** (Cột) | Vector Cột ($M \times v$) | `Hàm lồng nhau (hiệu ứng đọc từ phải sang trái)` |
| **OpenTK (Matrix4)** | **Row-Major** (Hàng) | Vector Hàng ($v \times M$) | `Scale * Translation` (Trái sang Phải) |

**Trực quan hóa:**
Nếu bạn muốn **Co giãn** (Scale - S) một vật rồi **Tịnh tiến** (Translate - T) nó:
*   **Toán học**: $P' = T \cdot S \cdot P$ (Phép Tịnh tiến xảy ra "sau" khi điểm P đã được Co giãn)
*   **Code OpenTK**: `Matrix4 final = Matrix4.CreateScale(S) * Matrix4.CreateTranslation(T);` khớp với thứ tự đọc tự nhiên (Scale rồi mới Translate).

**Mẹo chuyên nghiệp (Pro-Tip)**: Khi chuyển code từ JS sang C#, **đừng bao giờ mặc định `Matrix.Multiply` hoạt động giống nhau.** Hãy luôn kiểm chứng thứ tự logic: "Tôi muốn xoay vật thể quanh tâm của nó (Xoay rồi Tịnh tiến), hay muốn nó bay quanh gốc tọa độ thế giới (Tịnh tiến rồi Xoay)?"

---

## 4. Hệ tọa độ: Mercator và OpenGL

Card đồ họa không hiểu "Kinh độ/Vĩ độ" hay "Mét". Chúng chỉ hiểu **Clip Space** $(-1 \text{ đến } 1)$.

**Thách thức**:
*   **Địa lý**: Kinh độ $(-180 \text{ đến } +180)$, Vĩ độ $(\approx -85 \text{ đến } +85)$.
*   **Đích đến**: $X [-1, 1], Y [-1, 1]$.

**Giải pháp của chúng ta**:
1.  **Chuẩn hóa (Normalize)**: Chuyển Lat/Lng về khoảng $0.0 \rightarrow 1.0$ (Phép chiếu Web Mercator).
2.  **Căn tâm (Center)**: Điều chỉnh về $-0.5 \rightarrow 0.5$ (tương đối so với tâm).
3.  **Tỷ lệ (Scale)**: Nhân với Hệ số Zoom.
4.  **Kết quả**: Nếu giá trị nằm trong $[-1, 1]$, nó sẽ hiển thị.

**Cảnh báo: Độ chính xác số thực (Floating Point Precision)**
Ở mức Zoom 20, tọa độ thế giới rất lớn, nhưng sự khác biệt giữa các điểm lại rất nhỏ $(0.000001)$. Số thực 32-bit (Float) sẽ mất độ chính xác ở đây, gây ra hiện tượng "rung lắc" (jitter) đỉnh.
*   *Cách sửa*: Giữ tọa độ tương đối so với Camera hoặc tâm Tile trên CPU (dùng `double`), và chỉ gửi các giá trị nhỏ, tương đối (Float) xuống GPU.

---

## 5. Debug lỗi "Màn hình trắng" (Nghệ thuật Hắc ám)

Khi bạn thấy màn hình trắng, GPU thất bại trong im lặng. Hãy dùng danh sách kiểm tra này:

1.  **Bài kiểm tra "Tam giác đỏ"**: Quên dữ liệu phức tạp đi. Bạn có thể vẽ được một hình tam giác màu đỏ đơn giản được code cứng (hard-coded) không?
    *   *Có*: Shader/Cửa sổ setup tốt. Vấn đề nằm ở Dữ liệu hoặc Ma trận.
    *   *Không*: Shader chưa biên dịch được hoặc ngữ cảnh (context) không hợp lệ.
2.  **Phạm vi tọa độ**: Các đỉnh của bạn có nằm trong $[-1, 1]$ không?
    *   Log giá trị `gl_Position` tính toán được trên CPU trước khi gửi đi.
    *   *Lỗi đã tìm thấy*: Chúng ta phát hiện tọa độ hợp lệ nhưng quá nhỏ $(0.001)$. Cần phải zoom vào (sửa Camera Matrix).
3.  **Backface Culling (Loại bỏ mặt sau)**: Tam giác của bạn có đang quay lưng về phía bạn không?
    *   OpenGL mặc định thứ tự đỉnh ngược chiều kim đồng hồ là mặt trước. Nếu thứ tự đỉnh bị đảo lộn, tam giác sẽ tàng hình.
    *   *Thử*: `GL.Disable(EnableCap.CullFace)` xem nó có hiện ra không.
4.  **Z-Testing**: Nó có nằm sau camera không?
    *   *Thử*: `GL.Disable(EnableCap.DepthTest)` đối với bản đồ 2D.

---

## Tóm tắt cho Lập trình viên

*   **WebGL**: Linh hoạt, tích hợp trình duyệt.
*   **OpenTK**: Sức mạnh thô, kiểm soát chặt chẽ.
*   **Toán học**: Là phổ quát, nhưng **Cú pháp API** thì thay đổi.
*   **Luôn luôn**: Vẽ một tam giác debug trước tiên.

---

## 6. Các chủ đề nâng cao: Giao diện (UI) và Camera 3D

### Tích hợp Giao diện (ImGui)
Việc thêm Giao diện Người dùng (nút bấm, biểu đồ) vào ngữ cảnh OpenGL thô là rất khó khăn. Chúng tôi đã tích hợp **ImGui.NET** vì nó sử dụng cơ chế render **Immediate Mode** (Chế độ Trực tiếp):
- **Retained Mode (WPF/WinForms)**: Bạn tạo một đối tượng Nút, và framework sẽ ghi nhớ nó.
- **Immediate Mode (ImGui)**: Bạn viết `if (Button("Click Me")) { ... }` trong mỗi frame. Giao diện được xây dựng lại và vẽ từ đầu 60 lần mỗi giây.
- **Lợi ích**: Cực kỳ dễ dàng đồng bộ với trạng thái game. Không cần "event listeners" phức tạp cho các công cụ debug đơn giản.

### Render Bản đồ 3D
Chuyển từ bản đồ 2D sang 3D đòi hỏi Phép chiếu Phối cảnh (Perspective Projection):
- **Pitch (Độ nghiêng)**: Xoay quanh trục X. Chúng tôi giới hạn góc này khoảng 85° vì nếu nhìn bản đồ phẳng ở góc 90° (nhìn ngang cạnh), sẽ gây ra hiện tượng Z-fighting và hình học bị biến mất.
- **Thứ tự Ma trận**: Trong OpenTK (Vector Hàng), thứ tự cực kỳ quan trọng:
  `World = Scale * Translate`
  `View = RotateZ(Bearing) * RotateX(Pitch) * Translate(0, 0, -Altitude)`
  Điều này thực chất là "di chuyển thế giới ra xa" khỏi camera tĩnh.

### Tương tác: Ray Casting (Sửa lỗi "Trôi" Map)
Khi zoom trong 2D, ta chỉ đơn giản là unproject $(X, Y)$. Trong 3D (khi camera nghiêng), cách này sai vì khoảng cách từ camera xuống "mặt đất" thay đổi tùy theo vị trí trên màn hình (gần thì thấp, xa thì cao).
*   **Vấn đề**: Unproject ngây thơ giả định Z (độ sâu) là hằng số.
*   **Giải pháp**: **Giao điểm Tia-Mặt phẳng (Ray-Plane Intersection)**.
    1.  Chuyển đổi chuột $(X, Y)$ thành một **Tia (Ray)** trong không gian thế giới (Điểm Gần $\rightarrow$ Điểm Xa).
    2.  Định nghĩa Mặt phẳng Bản đồ là $Z = 0$.
    3.  Tính toán chính xác thời điểm $T$ mà Tia cắt Mặt phẳng.
    4.  Điều này cho ta Tọa độ Thế giới chính xác ngay dưới con trỏ chuột, đảm bảo bản đồ không bị "trượt" đi khi zoom lúc đang nghiêng.

### Độ Chính Xác & Hệ Tọa Độ
*   **Kích thước Viewport**: Luôn sử dụng `ClientSize` (OpenTK) hoặc `InnerWidth/Height` (Web). Sử dụng `Size` (kích thước cửa sổ tổng) bao gồm cả tiêu đề và viền, gây ra sai lệch tọa độ (ví dụ: chuột ở (0,0) thực ra là (0,30)).
*   **Double vs Float**: GPU xử lý tốt với `float` (Matrix4) vì các đỉnh (vertex) được tính tương đối so với camera. Tuy nhiên, để **Picking** (ScreenToWorld) ở Zoom 20, sự chênh lệch tọa độ nhỏ hơn độ chính xác của `float` ($10^{-7}$).
    *   **Giải pháp**: Cài đặt một luồng xử lý `Matrix4d` (Double Precision) riêng biệt cho tính toán trên CPU (Picking/Panning). Không dựa vào việc nghịch đảo `Matrix4` ở mức zoom lớn.

