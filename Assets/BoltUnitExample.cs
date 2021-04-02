using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SS;
using Bolt;
using Ludiq;

namespace SS
{
    [Ludiq.IncludeInSettings(true)]
    public static class BoltFunctionExample
    {
        public static Vector2 Function(Vector2 x, Vector2 y)
        {
            return x + y;
        }
    }

    [UnitCategory("SS")]
    [UnitTitle("Bolt Unit Example")]
    [UnitShortTitle("Bolt Unit Example")]
    public class BoltUnitExample : Unit
    {
        public int intValue;
        public float floatValue;
        public string stringValue;

        [DoNotSerialize]
        [PortLabel("Value Input")]
        public ValueInput valueInput;

        [DoNotSerialize]
        [PortLabel("String Input")]
        public ValueInput valueInputString;

        [DoNotSerialize]
        [PortLabel("Value Output")]
        public ValueOutput valueOutput;
        [DoNotSerialize]
        [PortLabel("Control Input")]
        public ControlInput controlInput;
        [DoNotSerialize]
        [PortLabel("Control Output")]
        public ControlOutput controlOutput;

        protected override void Definition()
        {
            //
            controlInput = ControlInput(nameof(controlInput), ControlInputFunc);
            controlOutput = ControlOutput("Control Output");
            valueInput = ValueInput<int>("Value Input", -1);
            valueInputString = ValueInput<string>(nameof(valueInputString), null);
            valueOutput = ValueOutput(typeof(int), "Value output", ValueOutputFunc);

            // Relation
            Succession(controlInput, controlOutput);
            Requirement(valueInput, valueOutput);
        }

        private ControlOutput ControlInputFunc(Flow flow)
        {
            Debug.Log("ControlInputFunc " + flow.GetValue<int>(valueInput));
            return controlOutput;
        }

        private object ValueOutputFunc(Flow flow)
        {
            return null;
        }

    }

}