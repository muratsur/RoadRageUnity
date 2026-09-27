using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RoadRage.UnityRemake
{
    public static class GameInput
    {
        public static bool GetEscapePressed()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
                var pad = UnityEngine.InputSystem.Gamepad.current;
                if (pad != null && (pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame)) return true;
            }
            catch {}
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape)) return true;
            }
            catch {}
            return false;
        }

        /// Launch/confirm ("START RUN"): Space or Enter on the keyboard, Start or the south
        /// face button on a gamepad. Same dual new-Input-System / legacy fallback pattern as
        /// the rest of GameInput, so it works whatever Active Input Handling is set to.
        public static bool GetStartPressed()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && (kb.spaceKey.wasPressedThisFrame
                    || kb.enterKey.wasPressedThisFrame
                    || kb.numpadEnterKey.wasPressedThisFrame)) return true;
                var pad = UnityEngine.InputSystem.Gamepad.current;
                if (pad != null && (pad.startButton.wasPressedThisFrame
                    || pad.buttonSouth.wasPressedThisFrame)) return true;
            }
            catch {}
            try
            {
                if (Input.GetKeyDown(KeyCode.Space)
                    || Input.GetKeyDown(KeyCode.Return)
                    || Input.GetKeyDown(KeyCode.KeypadEnter)) return true;
            }
            catch {}
            return false;
        }

        public static bool GetBKeyPressed()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.bKey.wasPressedThisFrame) return true;
            }
            catch {}
            try
            {
                if (Input.GetKeyDown(KeyCode.B)) return true;
            }
            catch {}
            return false;
        }

        public static bool GetNKeyPressed()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.nKey.wasPressedThisFrame) return true;
            }
            catch {}
            try
            {
                if (Input.GetKeyDown(KeyCode.N)) return true;
            }
            catch {}
            return false;
        }

        /// Cycles weather. Weather is rolled at random per run, and storm halves the sun
        /// while doubling the fog, so two runs of the same biome are not comparable unless
        /// the weather is pinned - which quietly invalidated every A/B of the lighting.
        /// P logs the render cost of the loaded world - the measurement Gate A was
        /// decided on. Not a debug flag: foliage work has to be judged on this number
        /// and it should not need a command-line argument to read.
        public static bool GetPKeyPressed()
        {
        	try
        	{
        		var kb = UnityEngine.InputSystem.Keyboard.current;
        		if (kb != null && kb.pKey.wasPressedThisFrame) return true;
        	}
        	catch {}
        	try
        	{
        		if (Input.GetKeyDown(KeyCode.P)) return true;
        	}
        	catch {}
        	return false;
        }

        public static bool GetKKeyPressed()
        {
        	try
        	{
        		var kb = UnityEngine.InputSystem.Keyboard.current;
        		if (kb != null && kb.kKey.wasPressedThisFrame) return true;
        	}
        	catch {}
        	try
        	{
        		if (Input.GetKeyDown(KeyCode.K)) return true;
        	}
        	catch {}
        	return false;
        }

        public static bool GetGKeyPressed()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.gKey.wasPressedThisFrame) return true;
            }
            catch {}
            try
            {
                if (Input.GetKeyDown(KeyCode.G)) return true;
            }
            catch {}
            return false;
        }

        public static bool GetNumberKey(int digit)
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    var pressed = digit switch
                    {
                        1 => kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame,
                        2 => kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame,
                        3 => kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame,
                        4 => kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame,
                        5 => kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame,
                        6 => kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame,
                        7 => kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame,
                        8 => kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame,
                        9 => kb.digit9Key.wasPressedThisFrame || kb.numpad9Key.wasPressedThisFrame,
                        0 => kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame,
                        _ => false
                    };
                    if (pressed) return true;
                }
            }
            catch {}

            try
            {
                return digit switch
                {
                    1 => Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1),
                    2 => Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2),
                    3 => Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3),
                    4 => Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4),
                    5 => Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5),
                    6 => Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6),
                    7 => Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7),
                    8 => Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8),
                    9 => Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9),
                    0 => Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0),
                    _ => false
                };
            }
            catch { return false; }
        }

        public static float GetSteer()
        {
            var steer = 0f;
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
                }
                var pad = UnityEngine.InputSystem.Gamepad.current;
                if (pad != null)
                {
                    var stick = pad.leftStick.x.ReadValue();
                    var dpad = pad.dpad.x.ReadValue();
                    if (Mathf.Abs(stick) > 0.12f) steer += stick;
                    else if (Mathf.Abs(dpad) > 0.12f) steer += dpad;
                }
            }
            catch {}

            try
            {
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) steer -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) steer += 1f;
                var axis = Input.GetAxisRaw("Horizontal");
                if (Mathf.Abs(axis) > 0.1f) steer += axis;
            }
            catch {}

            return Mathf.Clamp(steer, -1f, 1f);
        }

        public static float GetThrottle()
        {
            var throttle = 0f;
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed || kb.spaceKey.isPressed) throttle -= 1f;
                }
                var pad = UnityEngine.InputSystem.Gamepad.current;
                if (pad != null)
                {
                    var rt = pad.rightTrigger.ReadValue();
                    var lt = pad.leftTrigger.ReadValue();
                    if (rt > 0.05f) throttle += rt;
                    if (lt > 0.05f) throttle -= lt;
                    if (pad.buttonSouth.isPressed) throttle += 1f; // A button
                    if (pad.buttonEast.isPressed || pad.buttonWest.isPressed) throttle -= 1f; // B/X button
                    var stickY = pad.leftStick.y.ReadValue();
                    if (Mathf.Abs(stickY) > 0.15f) throttle += stickY;
                }
            }
            catch {}

            try
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) throttle += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.Space)) throttle -= 1f;
                var axis = Input.GetAxisRaw("Vertical");
                if (Mathf.Abs(axis) > 0.1f) throttle += axis;
            }
            catch {}

            return Mathf.Clamp(throttle, -1f, 1f);
        }
    }
}
