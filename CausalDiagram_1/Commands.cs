using System;
using System.Collections.Generic;
using System.Linq;


namespace CausalDiagram_1
{
    public interface ICommand
    {
        void Execute();
        void Undo();
    }

    public class CommandManager
    {
        private readonly Stack<ICommand> _undo = new Stack<ICommand>();
        private readonly Stack<ICommand> _redo = new Stack<ICommand>();

        public void ExecuteCommand(ICommand c)
        {
            c.Execute();
            _undo.Push(c);
            _redo.Clear();
        }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public void Undo()
        {
            if (!CanUndo) return;
            var c = _undo.Pop();
            c.Undo();
            _redo.Push(c);
        }

        public void Redo()
        {
            if (!CanRedo) return;
            var c = _redo.Pop();
            c.Execute();
            _undo.Push(c);
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }

    // AddNode
    public class AddNodeCommand : ICommand
    {
        private readonly Diagram _diagram;
        private readonly Node _node;

        public AddNodeCommand(Diagram diagram, Node node)
        {
            _diagram = diagram;
            _node = node;
        }

        public void Execute() => _diagram.Nodes.Add(_node);
        public void Undo() => _diagram.Nodes.Remove(_node);
    }

    // Remove node (and store linked edges)
    public class RemoveNodeCommand : ICommand
    {
        private readonly Diagram _diagram;
        private readonly Node _node;
        private readonly List<Edge> _removedEdges = new List<Edge>();

        public RemoveNodeCommand(Diagram diagram, Node node)
        {
            _diagram = diagram;
            _node = node;
        }

        public void Execute()
        {
            _removedEdges.AddRange(_diagram.Edges.FindAll(e => e.From == _node.Id || e.To == _node.Id));
            foreach (var e in _removedEdges) _diagram.Edges.Remove(e);
            _diagram.Nodes.Remove(_node);
        }

        public void Undo()
        {
            _diagram.Nodes.Add(_node);
            _diagram.Edges.AddRange(_removedEdges);
            _removedEdges.Clear();
        }
    }

    public class AddEdgeCommand : ICommand
    {
        private readonly Diagram _diagram;
        private readonly Edge _edge;

        public AddEdgeCommand(Diagram diagram, Edge edge)
        {
            _diagram = diagram;
            _edge = edge;
        }

        public void Execute() => _diagram.Edges.Add(_edge);
        public void Undo() => _diagram.Edges.Remove(_edge);
    }

    public class MoveNodeCommand : ICommand
    {
        private readonly Node _node;
        private readonly float _oldX, _oldY;
        private readonly float _newX, _newY;

        public MoveNodeCommand(Node node, float oldX, float oldY, float newX, float newY)
        {
            _node = node;
            _oldX = oldX;
            _oldY = oldY;
            _newX = newX;
            _newY = newY;
        }

        public void Execute()
        {
            _node.X = _newX;
            _node.Y = _newY;
        }

        public void Undo()
        {
            _node.X = _oldX;
            _node.Y = _oldY;
        }
    }

    // командa редактирования свойств узла — сохраняет старое и новое состояние узла
    public class EditNodePropertiesCommand : ICommand
    {
        private readonly Node _node;
        private readonly Node _oldSnapshot;
        private readonly Node _newSnapshot;

        public EditNodePropertiesCommand(Node node, Node oldSnapshot, Node newSnapshot)
        {
            _node = node;
            _oldSnapshot = oldSnapshot;
            _newSnapshot = newSnapshot;
        }

        public void Execute()
        {
            Apply(_newSnapshot);
        }

        public void Undo()
        {
            Apply(_oldSnapshot);
        }

        private void Apply(Node s)
        {
            // применяем по полям (не меняем Id,X,Y)
            _node.Title = s.Title;
            _node.Description = s.Description;
            _node.Weight = s.Weight;
            _node.ColorName = s.ColorName;
            _node.Severity = s.Severity;
            _node.Occurrence = s.Occurrence;
            _node.Detectability = s.Detectability;
        }
    }

    // ChangeNodeColorCommand — смена цвета узла с поддержкой undo/redo
    public class ChangeNodeColorCommand : ICommand
    {
        private readonly Node _node;
        private readonly NodeColor _oldColor;
        private readonly NodeColor _newColor;

        public ChangeNodeColorCommand(Node node, NodeColor newColor)
        {
            _node = node;
            _oldColor = node.ColorName;
            _newColor = newColor;
        }

        public void Execute()
        {
            _node.ColorName = _newColor;
        }

        public void Undo()
        {
            _node.ColorName = _oldColor;
        }
    }

    // RemoveEdgeCommand — удаление ребра (undo/redo)
    public class RemoveEdgeCommand : ICommand
    {
        private readonly Diagram _diagram;
        private readonly Edge _edge;

        public RemoveEdgeCommand(Diagram diagram, Edge edge)
        {
            _diagram = diagram;
            // делаем копию ребра (на всякий случай)
            _edge = new Edge { Id = edge.Id, From = edge.From, To = edge.To };
        }

        public void Execute()
        {
            var existing = _diagram.Edges.FirstOrDefault(e => e.Id == _edge.Id);
            if (existing != null) _diagram.Edges.Remove(existing);
        }

        public void Undo()
        {
            // восстановим, если не существует
            if (!_diagram.Edges.Any(e => e.Id == _edge.Id))
            {
                _diagram.Edges.Add(_edge);
            }
        }
    }

}
