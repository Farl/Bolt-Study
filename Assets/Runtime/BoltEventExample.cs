using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Bolt;
using Ludiq;
using System;

namespace SS
{
    [UnitCategory("SS")]
    [UnitTitle("Bolt Event Example EX")]
    [UnitShortTitle("Bolt Event Example")]
    [SpecialUnit]
    public sealed class BoltEventExample : Bolt.Unit, Bolt.IEventUnit
    {
        [DoNotSerialize]
        [PortLabel("Control Output")]
        public ControlOutput controlOutput;

        [DoNotSerialize, PortLabel("ASDF")]
        public ValueInput valueInput { get; private set; }

        [Inspectable]
        public string testStr;

        [UnitHeaderInspectable("TEST")]
        public string testStrHeader;

        [Serialize]
        [Inspectable]
        [InspectorExpandTooltip]
        public bool coroutine { get; private set; }

        private bool isListening;
        private GraphReference selfReference;
        private Coroutine currCoroutine;

        // Very important
        [Inspectable]
        public override bool isControlRoot { get; protected set; } = true;


        public bool IsListening(GraphPointer pointer) => isListening;

        public void StartListening(GraphStack stack)
        {
            Debug.Log("Start");
            isListening = true;

            var fm = selfReference.self.GetComponent<FlowMachine>();
            if (fm)
            {
                currCoroutine = fm.StartCoroutine(DoCoroutine());
            }
        }

        IEnumerator DoCoroutine()
        {
            while (isListening)
            {
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    Debug.Log("Shoot");
                    OnEvent();
                }

                yield return null;
            }
        }

        public void StopListening(GraphStack stack)
        {
            Debug.Log("Stop");
            isListening = false;
            if (currCoroutine != null)
            {
                var fm = selfReference.self.GetComponent<FlowMachine>();
                if (fm)
                {
                    fm.StopCoroutine(currCoroutine);
                }
            }
        }

        void OnEvent()
        {
            var flow = Flow.New(selfReference);
            flow.Run(controlOutput);
        }

        public override void Instantiate(GraphReference instance)
        {
            Debug.Log("Instantiate");
            base.Instantiate(instance);
            selfReference = instance;
        }

        public override void Uninstantiate(GraphReference instance)
        {
            Debug.Log("Uninstantiate");
            selfReference = default;
            base.Uninstantiate(instance);
        }

        protected override void Definition()
        {
            Debug.Log("Definition");
            controlOutput = ControlOutput(nameof(controlOutput));
            valueInput = ValueInput<string>(nameof(valueInput), default);
        }
    }
}
