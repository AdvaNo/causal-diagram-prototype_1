using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CausalDiagram_1
{
    public enum NodeColor
    {
        Green,
        Yellow,
        Red
    }

    public class Node
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = "Узел";
        public string Description { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Weight { get; set; } = 0f;

        // FMEA поля
        public int Severity { get; set; } = 1;
        public int Occurrence { get; set; } = 1;
        public int Detectability { get; set; } = 1;

        // Цвет и форма (сериализуется как enum)
        public NodeColor ColorName { get; set; } = NodeColor.Green;

        [JsonIgnore]
        public int Rpn => Severity * Occurrence * Detectability;

        [JsonIgnore] // чтобы не сериализовать визуальные состояния
        public bool IsHighlighted { get; set; } = false;
    }

    public class Edge
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid From { get; set; }
        public Guid To { get; set; }

        [JsonIgnore]
        public bool IsHighlighted { get; set; } = false;
    }

    public class Diagram
    {
        public List<Node> Nodes { get; set; } = new List<Node>();
        public List<Edge> Edges { get; set; } = new List<Edge>();
    }
}
