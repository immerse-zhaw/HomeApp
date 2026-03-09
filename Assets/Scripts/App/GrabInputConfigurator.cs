using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace App
{
    public static class GrabInputConfigurator
    {
        private const string LeftControllerTriggerButtonPath = "<XRController>{LeftHand}/{TriggerButton}";
        private const string RightControllerTriggerButtonPath = "<XRController>{RightHand}/{TriggerButton}";

        public static void Configure(bool allowTriggerGrab)
        {
            int configuredCount = 0;

            foreach (var interactor in UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (TryConfigure(interactor, allowTriggerGrab))
                {
                    configuredCount++;
                }
            }

            foreach (var interactor in UnityEngine.Object.FindObjectsByType<XRDirectInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (TryConfigure(interactor, allowTriggerGrab))
                {
                    configuredCount++;
                }
            }

            Debug.Log($"[GrabInputConfigurator] {(allowTriggerGrab ? "Enabled" : "Disabled")} trigger grab on {configuredCount} interactor(s).");
        }

        private static bool TryConfigure(XRBaseInputInteractor interactor, bool allowTriggerGrab)
        {
            if (interactor == null)
            {
                return false;
            }

            var performedAction = interactor.selectInput.inputActionReferencePerformed?.action ?? interactor.selectInput.inputActionPerformed;
            var valueAction = interactor.selectInput.inputActionReferenceValue?.action ?? interactor.selectInput.inputActionValue;

            if (performedAction == null || valueAction == null)
            {
                Debug.LogWarning($"[GrabInputConfigurator] Skipping {interactor.name}: missing select input actions.");
                return false;
            }

            interactor.selectInput = allowTriggerGrab
                ? CreateDefaultReader(performedAction, valueAction)
                : CreateGripOnlyReader(performedAction, valueAction);

            return true;
        }

        private static XRInputButtonReader CreateDefaultReader(InputAction performedAction, InputAction valueAction)
        {
            return new XRInputButtonReader("Select")
            {
                inputSourceMode = XRInputButtonReader.InputSourceMode.InputActionReference,
                inputActionReferencePerformed = InputActionReference.Create(performedAction),
                inputActionReferenceValue = InputActionReference.Create(valueAction),
            };
        }

        private static XRInputButtonReader CreateGripOnlyReader(InputAction performedAction, InputAction valueAction)
        {
            return new XRInputButtonReader("Select")
            {
                inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction,
                inputActionPerformed = CloneAction(performedAction, $"{performedAction.actionMap?.name ?? performedAction.name} Grip Select", ShouldKeepGripSelectBinding),
                inputActionValue = CloneAction(valueAction, $"{valueAction.actionMap?.name ?? valueAction.name} Grip Select Value"),
            };
        }

        private static bool ShouldKeepGripSelectBinding(InputBinding binding)
        {
            return !string.Equals(binding.path, LeftControllerTriggerButtonPath, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(binding.path, RightControllerTriggerButtonPath, StringComparison.OrdinalIgnoreCase);
        }

        private static InputAction CloneAction(InputAction sourceAction, string name, Func<InputBinding, bool> bindingFilter = null)
        {
            var clonedAction = new InputAction(
                name,
                sourceAction.type,
                expectedControlType: sourceAction.expectedControlType,
                interactions: sourceAction.interactions,
                processors: sourceAction.processors);

            foreach (var binding in sourceAction.bindings)
            {
                if (bindingFilter != null && !bindingFilter(binding))
                {
                    continue;
                }

                clonedAction.AddBinding(binding);
            }

            return clonedAction;
        }
    }
}