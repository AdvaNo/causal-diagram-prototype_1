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

        // FMEA поля: ПОКА НЕ НУЖНЫ
        public int Severity { get; set; } = 1;
        public int Occurrence { get; set; } = 1;
        public int Detectability { get; set; } = 1;

        // Цвет и форма
        public NodeColor ColorName { get; set; } = NodeColor.Green;

        [JsonIgnore]
        public int Rpn => Severity * Occurrence * Detectability;

        [JsonIgnore] // чтобы не сериализовать визуальные состояния
        public bool IsHighlighted { get; set; } = false;

        public NodeCategory Category { get; set; } = NodeCategory.Компонент;
    }

    public class Edge
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid From { get; set; }
        public Guid To { get; set; }

        [JsonIgnore]
        public bool IsHighlighted { get; set; } = false;

        //[JsonIgnore]
        public bool IsForbidden { get; set; } = false;
    }

    public class Diagram
    {
        public List<Node> Nodes { get; set; } = new List<Node>();
        public List<Edge> Edges { get; set; } = new List<Edge>();

        // добавить правила категоризации в модель, чтобы сохранять/загружать вместе с файлом
        public List<ForbiddenRule> ForbiddenRules { get; set; } = new List<ForbiddenRule>();
    }

    public enum NodeCategory
    {
        Системные = 0,
        Подсистемные = 1,
        Компонент = 2,
        Процесс = 3,
        Человек = 4
    }

    public class ForbiddenRule
    {
        public NodeCategory FromCategory { get; set; }
        public NodeCategory ToCategory { get; set; }
        public string Reason { get; set; } = "";

        public override string ToString()
        {
            return $"{FromCategory} → {ToCategory}" + (string.IsNullOrWhiteSpace(Reason) ? "" : $": {Reason}");
        }
    }

}
