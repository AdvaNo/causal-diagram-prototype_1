using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace CausalDiagram_1
{
    public class MainForm : Form
    {
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        //отладка

        private Diagram _diagram = new Diagram();
        private readonly CommandManager _cmd = new CommandManager();

        // Размеры узла (прямоугольник)
        private const int NodeWidth = 120;
        private const int NodeHeight = 60;

        // Текущий цвет для создания новых узлов
        private NodeColor _currentColor = NodeColor.Green;

        // Кнопка "Выбрать" (мышка) - объявите поле чтобы можно было подсвечивать
        private Button _btnSelect;

        // UI
        private Panel _canvas;
        private ToolStrip _tool;
        private PropertyGrid _propGrid;
        private Button _btnAddNode, _btnConnect, _btnDelete, _btnUndo, _btnRedo, _btnFmea;
        private string _currentFile;

        // Interaction state
        private enum Mode { Select, AddNode, Connect, Pan }
        private Mode _mode = Mode.Select;
        private Node _dragNode = null;
        private PointF _dragStart;
        private bool _isPanning = false;
        private float _scale = 1.0f;
        private PointF _panOffset = new PointF(0, 0);
        private Node _connectFrom = null;

        private const int NodeRadius = 40;

        public MainForm()
        {
            Text = "Автомат — причинно-следственная диаграмма (прототип)";
            Width = 1200;
            Height = 800;

            InitializeUi();
        }

        private void InitializeUi()
        {
            // ToolStrip
            _tool = new ToolStrip();
            var tsiNew = new ToolStripButton("Новый");
            var tsiOpen = new ToolStripButton("Открыть");
            var tsiSave = new ToolStripButton("Сохранить");
            var tsiExport = new ToolStripButton("Экспорт PNG");

            tsiNew.Click += (s, e) => NewDiagram();
            tsiOpen.Click += (s, e) => OpenDiagram();
            tsiSave.Click += (s, e) => SaveDiagram();
            tsiExport.Click += (s, e) => ExportPng();

            // создаём управляющие кнопки (включая Select и цветовые кнопки)
            _btnSelect = new Button { Text = "Выбрать" };
            _btnAddNode = new Button { Text = "Добавить узел" };
            _btnConnect = new Button { Text = "Соединить" };
            _btnDelete = new Button { Text = "Удалить" };
            _btnUndo = new Button { Text = "Отменить" };
            _btnRedo = new Button { Text = "Повторить" };
            _btnFmea = new Button { Text = "FMEA (RPN)" };

            // события
            _btnSelect.Click += (s, e) => SetMode(Mode.Select);
            _btnAddNode.Click += (s, e) => SetMode(Mode.AddNode);
            _btnConnect.Click += (s, e) => SetMode(Mode.Connect);
            _btnDelete.Click += (s, e) => DeleteSelected();
            _btnUndo.Click += (s, e) => { _cmd.Undo(); InvalidateCanvas(); };
            _btnRedo.Click += (s, e) => { _cmd.Redo(); InvalidateCanvas(); };
            _btnFmea.Click += (s, e) => ShowFmeaForm();

            // Цветовые кнопки (ToolStrip кнопки удобнее, но мы используем обычные Button-hosts)
            var btnColorGreen = new Button { Text = "Зелёный" };
            var btnColorYellow = new Button { Text = "Жёлтый" };
            var btnColorRed = new Button { Text = "Красный" };

            btnColorGreen.Click += (s, e) => { _currentColor = NodeColor.Green; UpdateColorButtons(btnColorGreen, btnColorYellow, btnColorRed); };
            btnColorYellow.Click += (s, e) => { _currentColor = NodeColor.Yellow; UpdateColorButtons(btnColorGreen, btnColorYellow, btnColorRed); };
            btnColorRed.Click += (s, e) => { _currentColor = NodeColor.Red; UpdateColorButtons(btnColorGreen, btnColorYellow, btnColorRed); };

            // hosts
            var hostSelect = new ToolStripControlHost(_btnSelect);
            var hostAdd = new ToolStripControlHost(_btnAddNode);
            var hostConnect = new ToolStripControlHost(_btnConnect);
            var hostDelete = new ToolStripControlHost(_btnDelete);
            var hostUndo = new ToolStripControlHost(_btnUndo);
            var hostRedo = new ToolStripControlHost(_btnRedo);
            var hostFmea = new ToolStripControlHost(_btnFmea);

            var hostColorG = new ToolStripControlHost(btnColorGreen);
            var hostColorY = new ToolStripControlHost(btnColorYellow);
            var hostColorR = new ToolStripControlHost(btnColorRed);

            // добавление в тулбар (порядок можно менять)
            _tool.Items.Add(tsiNew);
            _tool.Items.Add(tsiOpen);
            _tool.Items.Add(tsiSave);
            _tool.Items.Add(tsiExport);
            _tool.Items.Add(new ToolStripSeparator());

            _tool.Items.Add(hostSelect);
            _tool.Items.Add(hostAdd);
            _tool.Items.Add(hostConnect);
            _tool.Items.Add(hostDelete);
            _tool.Items.Add(hostUndo);
            _tool.Items.Add(hostRedo);
            _tool.Items.Add(hostFmea);

            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(new ToolStripLabel("Цвет:"));
            _tool.Items.Add(hostColorG);
            _tool.Items.Add(hostColorY);
            _tool.Items.Add(hostColorR);

            // установить визуальную подсветку цвета по-умолчанию
            UpdateColorButtons(btnColorGreen, btnColorYellow, btnColorRed);

            // --- PropertyGrid (справа) ---
            _propGrid = new PropertyGrid { Dock = DockStyle.Right, Width = 300 };
            // чтобы изменения в PropertyGrid сразу перерисовывали холст
            _propGrid.PropertyValueChanged += (s, e) => InvalidateCanvas();

            // --- Canvas (центр) ---
            _canvas = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            _canvas.Paint += Canvas_Paint;
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseWheel += Canvas_MouseWheel;
            _canvas.Resize += (s, e) => InvalidateCanvas();
            // включаем double buffering (метод из расширения)
            this.DoubleBufferedControl(_canvas, true);

            // --- StatusStrip (внизу) ---
            _statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("Готово");
            _statusStrip.Items.Add(_statusLabel);

            // --- Добавляем элементы управления на форму в правильном порядке ---
            Controls.Add(_canvas);       // центр
            Controls.Add(_propGrid);    // справа
            Controls.Add(_tool);        // сверху
            Controls.Add(_statusStrip); // снизу

            // Установим режим по умолчанию
            _mode = Mode.Select;

            // Обновим статус (и перерисовку)
            UpdateStatus();
            UpdateStatusText();
            InvalidateCanvas();
        }

        private void UpdateColorButtons(Button g, Button y, Button r)
        {
            // простая визуальная подсветка: выбранная кнопка — светло-зелёная, прочие — стандарт
            g.BackColor = (_currentColor == NodeColor.Green) ? Color.LightGreen : SystemColors.Control;
            y.BackColor = (_currentColor == NodeColor.Yellow) ? Color.LightYellow : SystemColors.Control;
            r.BackColor = (_currentColor == NodeColor.Red) ? Color.LightCoral : SystemColors.Control;
        }
        private void UpdateStatusText()
        {
            try
            {
                _statusLabel.Text = $"Режим: {_mode} | Узлы: {_diagram?.Nodes?.Count ?? 0}";
            }
            catch { _statusLabel.Text = "Режим: ?"; }
        }


        private void SetMode(Mode m)
        {
            _mode = m;
            _connectFrom = null;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            _btnAddNode.BackColor = _mode == Mode.AddNode ? Color.LightGreen : SystemColors.Control;
            _btnConnect.BackColor = _mode == Mode.Connect ? Color.LightGreen : SystemColors.Control;
        }

        #region Canvas drawing & helpers

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // apply transform: scale then translate (pan)
                var m = g.Transform;
                m.Reset();
                m.Scale(_scale, _scale);
                m.Translate(_panOffset.X, _panOffset.Y);
                g.Transform = m;

                // draw edges
                foreach (var edge in _diagram.Edges)
                {
                    var from = _diagram.Nodes.FirstOrDefault(n => n.Id == edge.From);
                    var to = _diagram.Nodes.FirstOrDefault(n => n.Id == edge.To);
                    if (from == null || to == null) continue;
                    DrawArrow(g, new PointF(from.X, from.Y), new PointF(to.X, to.Y));
                }

                // draw nodes
                foreach (var node in _diagram.Nodes)
                {
                    DrawNode(g, node);
                }
            }
            catch (Exception ex)
            {
                // безопасный лог и уведомление
                try
                {
                    string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "causal_diagram_error.log");
                    System.IO.File.AppendAllText(tmp, DateTime.Now.ToString("s") + " — Canvas_Paint exception:\r\n" + ex.ToString() + "\r\n\r\n");
                }
                catch { /* негромкая неудача логирования */ }

                // покажем краткое сообщение — так мы увидим, что именно упало
                MessageBox.Show("Ошибка при отрисовке холста: " + ex.Message + "\nСм. лог: causal_diagram_error.log в %TEMP%", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DrawNode(Graphics g, Node node)
        {
            var center = new PointF(node.X, node.Y);
            var rect = new RectangleF(center.X - NodeWidth / 2f, center.Y - NodeHeight / 2f, NodeWidth, NodeHeight);

            Brush fill;
            Pen pen = Pens.DodgerBlue;

            switch (node.ColorName)
            {
                case NodeColor.Green:
                    fill = Brushes.LightGreen;
                    // острые углы — простая прямоугольная отрисовка
                    g.FillRectangle(fill, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    break;
                case NodeColor.Yellow:
                    fill = Brushes.LightYellow;
                    g.FillRectangle(fill, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    break;
                case NodeColor.Red:
                    fill = Brushes.LightCoral;
                    // закруглённые углы
                    using (var path = RoundedRectPath(rect, 12f))
                    {
                        g.FillPath(fill, path);
                        g.DrawPath(Pens.DarkRed, path);
                    }
                    break;
                default:
                    fill = Brushes.LightGray;
                    g.FillRectangle(fill, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    break;
            }

            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(node.Title, SystemFonts.DefaultFont, Brushes.Black, center, sf);
        }

        private GraphicsPath RoundedRectPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float r = Math.Max(0, radius);
            float diameter = r * 2f;

            // верхняя левая дуга
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            // верхняя правая дуга
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            // нижняя правая дуга
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            // нижняя левая дуга
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void DrawArrow(Graphics g, PointF pFrom, PointF pTo)
        {
            // получим точки на границах прямоугольников
            var fromRect = new RectangleF(pFrom.X - NodeWidth / 2f, pFrom.Y - NodeHeight / 2f, NodeWidth, NodeHeight);
            var toRect = new RectangleF(pTo.X - NodeWidth / 2f, pTo.Y - NodeHeight / 2f, NodeWidth, NodeHeight);

            var pt1 = GetRectBoundaryPointTowards(fromRect, pTo);
            var pt2 = GetRectBoundaryPointTowards(toRect, pFrom); // точка на границе целевого прямоугольника со стороны источника

            using (var pen = new Pen(Color.DarkGreen, 2))
            {
                // линия
                g.DrawLine(pen, pt1, pt2);

                // стрелка (треугольник) в конце pt2, направлен в сторону от pt1 к pt2
                var dir = new PointF(pt2.X - pt1.X, pt2.Y - pt1.Y);
                float len = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                if (len < 0.0001f) return;
                var ux = dir.X / len;
                var uy = dir.Y / len;

                float arrowLen = 12f;
                float halfWidth = 6f;

                // орт влево (перпендикуляр)
                var px = -uy;
                var py = ux;

                var a = new PointF(pt2.X, pt2.Y);
                var b = new PointF(pt2.X - ux * arrowLen + px * halfWidth, pt2.Y - uy * arrowLen + py * halfWidth);
                var c = new PointF(pt2.X - ux * arrowLen - px * halfWidth, pt2.Y - uy * arrowLen - py * halfWidth);

                g.FillPolygon(Brushes.DarkGreen, new[] { a, b, c });
            }
        }

        // Возвращает точку на границе rect в направлении к target
        private PointF GetRectBoundaryPointTowards(RectangleF rect, PointF target)
        {
            var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            var dx = target.X - center.X;
            var dy = target.Y - center.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.0001f) return center;

            var ux = dx / len;
            var uy = dy / len;

            // расстояние до границы по x и y
            float hx = rect.Width / 2f;
            float hy = rect.Height / 2f;

            float tx = Math.Abs(hx / ux);
            float ty = Math.Abs(hy / uy);

            float t = Math.Min(tx, ty);

            // вернём точку на границе
            return new PointF(center.X + ux * t, center.Y + uy * t);
        }


        private Node HitTestNode(PointF canvasPoint)
        {
            // проверяем, попадает ли точка в прямоугольник узла
            return _diagram.Nodes.FirstOrDefault(n =>
                Math.Abs(canvasPoint.X - n.X) <= NodeWidth / 2f &&
                Math.Abs(canvasPoint.Y - n.Y) <= NodeHeight / 2f);
        }

        private static float Distance(float x1, float y1, float x2, float y2)
        {
            var dx = x1 - x2;
            var dy = y1 - y2;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private void InvalidateCanvas() => _canvas.Invalidate();

        #endregion

        #region Mouse handling (add/move/connect/pan/zoom)

        private PointF ScreenToCanvas(Point screenPt)
        {
            return new PointF((screenPt.X / _scale) - _panOffset.X, (screenPt.Y / _scale) - _panOffset.Y);
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            _canvas.Focus();
            var p = ScreenToCanvas(e.Location);

            if (e.Button == MouseButtons.Middle)
            {
                _isPanning = true;
                _dragStart = e.Location;
                return;
            }

            if (_mode == Mode.AddNode && e.Button == MouseButtons.Left)
            {
                var node = new Node { X = p.X, Y = p.Y, Title = "Новый узел", ColorName = _currentColor };
                var cmd = new AddNodeCommand(_diagram, node);
                _cmd.ExecuteCommand(cmd);
                InvalidateCanvas();
                return;
            }

            if (_mode == Mode.Connect && e.Button == MouseButtons.Left)
            {
                var node = HitTestNode(p);
                if (node != null)
                {
                    if (_connectFrom == null)
                    {
                        _connectFrom = node;
                        return;
                    }
                    else if (_connectFrom != node)
                    {
                        var edge = new Edge { From = _connectFrom.Id, To = node.Id };
                        var cmd = new AddEdgeCommand(_diagram, edge);
                        _cmd.ExecuteCommand(cmd);
                        _connectFrom = null;
                        InvalidateCanvas();
                        return;
                    }
                }
            }

            // В режиме Select: выбор и/или перетаскивание узла
            if (_mode == Mode.Select && e.Button == MouseButtons.Left)
            {
                var node = HitTestNode(p);
                if (node != null)
                {
                    _dragNode = node;
                    _dragStart = new PointF(node.X, node.Y);
                    _propGrid.SelectedObject = new NodeProxy(node);
                    return;
                }
                else
                {
                    _propGrid.SelectedObject = null;
                }
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var p = ScreenToCanvas(e.Location);

            if (_isPanning && e.Button == MouseButtons.Middle)
            {
                var delta = new PointF((e.Location.X - _dragStart.X) / _scale, (e.Location.Y - _dragStart.Y) / _scale);
                _panOffset.X += delta.X;
                _panOffset.Y += delta.Y;
                _dragStart = e.Location;
                InvalidateCanvas();
                return;
            }

            if (_dragNode != null && e.Button == MouseButtons.Left)
            {
                _dragNode.X = p.X;
                _dragNode.Y = p.Y;
                InvalidateCanvas();
                return;
            }
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            var p = ScreenToCanvas(e.Location);

            if (_isPanning && e.Button == MouseButtons.Middle)
            {
                _isPanning = false;
                return;
            }

            if (_dragNode != null && e.Button == MouseButtons.Left)
            {
                var newX = _dragNode.X;
                var newY = _dragNode.Y;
                var cmd = new MoveNodeCommand(_dragNode, _dragStart.X, _dragStart.Y, newX, newY);
                _cmd.ExecuteCommand(cmd);
                _dragNode = null;
                InvalidateCanvas();
                return;
            }
        }

        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            var delta = e.Delta > 0 ? 1.1f : 0.9f;
            _scale *= delta;
            if (_scale < 0.2f) _scale = 0.2f;
            if (_scale > 4f) _scale = 4f;
            InvalidateCanvas();
        }

        #endregion

        #region File operations: New/Open/Save/Export

        private void NewDiagram()
        {
            if (AskSaveIfDirty()) return;
            _diagram = new Diagram();
            _cmd.Clear();
            _currentFile = null;
            InvalidateCanvas();
        }

        private bool AskSaveIfDirty()
        {
            var res = MessageBox.Show("Создать новый? Сохранить текущий файл?", "Новый", MessageBoxButtons.YesNoCancel);
            if (res == DialogResult.Cancel) return true;
            if (res == DialogResult.Yes) SaveDiagram();
            return false;
        }

        private void OpenDiagram()
        {
            using (var ofd = new OpenFileDialog { Filter = "Diagram files|*.cdg;*.json|All files|*.*" })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                var txt = File.ReadAllText(ofd.FileName);
                try
                {
                    _diagram = JsonConvert.DeserializeObject<Diagram>(txt) ?? new Diagram();
                    _currentFile = ofd.FileName;
                    _cmd.Clear();
                    InvalidateCanvas();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка при загрузке: " + ex.Message);
                }
            }
        }

        private void SaveDiagram()
        {
            if (string.IsNullOrEmpty(_currentFile))
            {
                using (var sfd = new SaveFileDialog { Filter = "Diagram files|*.cdg;*.json", FileName = "diagram.cdg" })
                {
                    if (sfd.ShowDialog() != DialogResult.OK) return;
                    _currentFile = sfd.FileName;
                }
            }

            try
            {
                var json = JsonConvert.SerializeObject(_diagram, Formatting.Indented);
                File.WriteAllText(_currentFile, json);
                MessageBox.Show("Сохранено.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при сохранении: " + ex.Message);
            }
        }

        private void ExportPng()
        {
            using (var sfd = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = "diagram.png" })
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;

                // создаём bitmap размером как текущая видимая область канваса
                int w = Math.Max(1, _canvas.Width);
                int h = Math.Max(1, _canvas.Height);

                using (var bmp = new Bitmap(w, h))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    // применяем ту же трансформацию, что и на холсте (scale и pan)
                    var m = g.Transform;
                    m.Reset();
                    m.Scale(_scale, _scale);
                    m.Translate(_panOffset.X, _panOffset.Y);
                    g.Transform = m;

                    // Рисуем ребра (стрелки) через ваш DrawArrow
                    foreach (var edge in _diagram.Edges)
                    {
                        var from = _diagram.Nodes.FirstOrDefault(n => n.Id == edge.From);
                        var to = _diagram.Nodes.FirstOrDefault(n => n.Id == edge.To);
                        if (from == null || to == null) continue;
                        DrawArrow(g, new PointF(from.X, from.Y), new PointF(to.X, to.Y));
                    }

                    // Рисуем узлы через ваш DrawNode (он уже рисует прямоугольники/закругления и цвет)
                    foreach (var node in _diagram.Nodes)
                    {
                        DrawNode(g, node);
                    }

                    // Сохраняем
                    bmp.Save(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
                }

                MessageBox.Show("Экспорт завершён.");
            }
        }

        #endregion

        #region Selection / delete / property grid helper

        private void DeleteSelected()
        {
            var sel = _propGrid.SelectedObject as NodeProxy;
            if (sel == null)
            {
                MessageBox.Show("Выберите узел в панели свойств.");
                return;
            }
            var node = sel.Node;
            var cmd = new RemoveNodeCommand(_diagram, node);
            _cmd.ExecuteCommand(cmd);
            _propGrid.SelectedObject = null;
            InvalidateCanvas();
        }

        private void ShowFmeaForm()
        {
            var f = new FmeaForm(_diagram);
            f.ShowDialog();
            InvalidateCanvas();
        }

        #endregion

        public class NodeProxy
        {
            public Node Node { get; }
            public NodeProxy(Node n) { Node = n; }

            public string Title { get => Node.Title; set => Node.Title = value; }
            public string Description { get => Node.Description; set => Node.Description = value; }
            public float Weight { get => Node.Weight; set => Node.Weight = value; }

            // expose enum - PropertyGrid отобразит как dropdown
            public NodeColor Color
            {
                get => Node.ColorName;
                set => Node.ColorName = value;
            }

            public int Severity { get => Node.Severity; set => Node.Severity = value; }
            public int Occurrence { get => Node.Occurrence; set => Node.Occurrence = value; }
            public int Detectability { get => Node.Detectability; set => Node.Detectability = value; }
        }

        // Reflection helper to enable double buffering on Panel
        private void DoubleBufferedControl(Control c, bool setting)
        {
            var prop = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop.SetValue(c, setting, null);
        }
    }
}
