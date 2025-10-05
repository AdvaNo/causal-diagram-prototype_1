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
        private Diagram _diagram = new Diagram();
        private readonly CommandManager _cmd = new CommandManager();

        // UI
        private Panel _canvas;
        private ToolStrip _tool;
        private PropertyGrid _propGrid;
        private Button _btnAddNode, _btnConnect, _btnDelete, _btnUndo, _btnRedo, _btnFmea;
        private string _currentFile;

        // Interaction state
        private enum Mode { None, AddNode, Connect, Pan }
        private Mode _mode = Mode.None;
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

            _btnAddNode = new Button { Text = "Добавить узел" };
            _btnConnect = new Button { Text = "Соединить" };
            _btnDelete = new Button { Text = "Удалить" };
            _btnUndo = new Button { Text = "Отменить" };
            _btnRedo = new Button { Text = "Повторить" };
            _btnFmea = new Button { Text = "FMEA (RPN)" };

            _btnAddNode.Click += (s, e) => SetMode(Mode.AddNode);
            _btnConnect.Click += (s, e) => SetMode(Mode.Connect);
            _btnDelete.Click += (s, e) => DeleteSelected();
            _btnUndo.Click += (s, e) => { _cmd.Undo(); InvalidateCanvas(); };
            _btnRedo.Click += (s, e) => { _cmd.Redo(); InvalidateCanvas(); };
            _btnFmea.Click += (s, e) => ShowFmeaForm();

            var host = new ToolStripControlHost(_btnAddNode);
            var host2 = new ToolStripControlHost(_btnConnect);
            var host3 = new ToolStripControlHost(_btnDelete);
            var host4 = new ToolStripControlHost(_btnUndo);
            var host5 = new ToolStripControlHost(_btnRedo);
            var host6 = new ToolStripControlHost(_btnFmea);

            _tool.Items.Add(tsiNew);
            _tool.Items.Add(tsiOpen);
            _tool.Items.Add(tsiSave);
            _tool.Items.Add(tsiExport);
            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(host);
            _tool.Items.Add(host2);
            _tool.Items.Add(host3);
            _tool.Items.Add(host4);
            _tool.Items.Add(host5);
            _tool.Items.Add(host6);

            Controls.Add(_tool);

            // PropertyGrid on the right
            _propGrid = new PropertyGrid { Dock = DockStyle.Right, Width = 300 };
            Controls.Add(_propGrid);

            // Canvas panel center
            _canvas = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            _canvas.Paint += Canvas_Paint;
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseWheel += Canvas_MouseWheel;
            _canvas.Resize += (s, e) => InvalidateCanvas();
            DoubleBufferedControl(_canvas, true);
            Controls.Add(_canvas);

            UpdateStatus();
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
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

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

        private void DrawNode(Graphics g, Node node)
        {
            var center = new PointF(node.X, node.Y);
            var rect = new RectangleF(center.X - NodeRadius, center.Y - NodeRadius, NodeRadius * 2, NodeRadius * 2);

            g.FillEllipse(Brushes.LightBlue, rect);
            g.DrawEllipse(Pens.DodgerBlue, rect);

            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(node.Title, SystemFonts.DefaultFont, Brushes.Black, center, sf);
        }


        private void DrawArrow(Graphics g, PointF p1, PointF p2)
        {
            using (var pen = new Pen(Color.DarkGreen, 2))
            {
                pen.EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor;
                g.DrawLine(pen, p1, p2);
            }
        }

        private Node HitTestNode(PointF canvasPoint)
        {
            return _diagram.Nodes.FirstOrDefault(n =>
                Distance(n.X, n.Y, canvasPoint.X, canvasPoint.Y) <= NodeRadius);
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
                var node = new Node { X = p.X, Y = p.Y, Title = "Новый узел" };
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

            if (e.Button == MouseButtons.Left)
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

                using (var bmp2 = new Bitmap(_canvas.Width, _canvas.Height))
                using (var g = Graphics.FromImage(bmp2))
                {
                    g.Clear(Color.White);
                    g.SmoothingMode = SmoothingMode.AntiAlias;

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
                        using (var pen = new Pen(Color.DarkGreen, 2))
                        {
                            pen.EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor;
                            g.DrawLine(pen, new PointF(from.X, from.Y), new PointF(to.X, to.Y));
                        }
                    }

                    // draw nodes
                    foreach (var node in _diagram.Nodes)
                    {
                        var center = new PointF(node.X, node.Y);
                        var rect = new RectangleF(center.X - NodeRadius, center.Y - NodeRadius, NodeRadius * 2, NodeRadius * 2);
                        g.FillEllipse(Brushes.LightBlue, rect);
                        g.DrawEllipse(Pens.DodgerBlue, rect);
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString(node.Title, SystemFonts.DefaultFont, Brushes.Black, center, sf);
                    }

                    bmp2.Save(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
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

        // Proxy for property grid
        public class NodeProxy
        {
            public Node Node { get; }
            public NodeProxy(Node n) { Node = n; }
            public string Title { get => Node.Title; set => Node.Title = value; }
            public string Description { get => Node.Description; set => Node.Description = value; }
            public float Weight { get => Node.Weight; set => Node.Weight = value; }
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
