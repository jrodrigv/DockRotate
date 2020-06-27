using System;
using System.Collections.Generic;

namespace DockRotate
{
	public static class DockingStateChecker
	{
		private class NodeState
		{
			private string state;
			private bool hasJoint, isSameVessel;

			private NodeState(string state, bool hasJoint, bool isSameVessel)
			{
				this.state = state;
				this.hasJoint = hasJoint;
				this.isSameVessel = isSameVessel;
			}

			private static readonly NodeState[] allowedNodeStates = new[] {
				new NodeState("Ready", false, false),
				new NodeState("Acquire", false, false),
				new NodeState("Acquire (dockee)", false, false),
				new NodeState("Disengage", false, false),
				new NodeState("Disabled", false, false),
				new NodeState("Docked (docker)", true, false),
				new NodeState("Docked (dockee)", true, false),
				new NodeState("Docked (dockee)", true, true),
				new NodeState("Docked (same vessel)", true, true),
				new NodeState("PreAttached", true, false)
			};

			public static NodeState find(ModuleDockingNode node)
			{
				if (!node)
					return null;
				string nodestate = node.fsm.currentStateName;
				bool hasJoint = node.getDockingJoint(out bool isSameVessel, false);
				for (int i = 0; i < allowedNodeStates.Length; i++) {
					ref NodeState s = ref allowedNodeStates[i];
					if (s.state == nodestate && s.hasJoint == hasJoint && s.isSameVessel == isSameVessel)
						return s;
				}
				return null;
			}
		}

		private class JointState
		{
			private string hoststate, targetstate;
			private bool isSameVessel;
			public Action<ModuleDockingNode, ModuleDockingNode> fixer;

			private JointState(string hoststate, string targetstate, bool isSameVessel,
				Action<ModuleDockingNode, ModuleDockingNode> fixer = null)
			{
				this.hoststate = hoststate;
				this.targetstate = targetstate;
				this.isSameVessel = isSameVessel;
				this.fixer = fixer;
			}

			private static readonly JointState[] allowedJointStates = new[] {
				new JointState("PreAttached", "PreAttached", false),
				new JointState("Docked (docker)", "Docked (dockee)", false),
				new JointState("Docked (dockee)", "Docked (docker)", false),
				new JointState("Docked (same vessel)", "Docked (dockee)", true),
				new JointState("Docked (dockee)", "Docked (dockee)", false, (host, target) => {
					host.DebugFSMState = target.DebugFSMState = true;
					host.fsm.StartFSM("Docked (docker)");
				})
			};

			public static JointState find(ModuleDockingNode host, ModuleDockingNode target, bool isSameVessel)
			{
				string hoststate = host.fsm.currentStateName;
				string targetstate = target.fsm.currentStateName;
				int l = JointState.allowedJointStates.GetLength(0);
				for (int i = 0; i < l; i++) {
					JointState s = JointState.allowedJointStates[i];
					if (s.hoststate == hoststate && s.targetstate == targetstate && s.isSameVessel == isSameVessel)
						return s;
				}
				return null;
			}
		};

		public static bool isBadNode(this ModuleDockingNode node, bool verbose)
		{
			if (!node)
				return false;

			bool foundError = false;
			List<string> msg = new List<string>();
			msg.Add(node.stateInfo());

			PartJoint j = node.getDockingJoint(out bool dsv, verbose);

			string label = "\"" + node.state + "\""
				+ (j ? ".hasJoint" : "")
				+ (dsv ? ".isSameVessel" : ".isTree");

			if (NodeState.find(node) == null)
				msg.Add("unallowed node state " + label);

			if (j)
				checkDockingJoint(msg, node, j, dsv);

			if (j && j.Host == node.part && node.vesselInfo == null
					&& node.state != "PreAttached" && node.state != "Docked (same vessel)")
				msg.Add("null vesselInfo");

			if (msg.Count > 1) {
				foundError = true;
			} else if (verbose) {
				msg.Add("is ok");
			}

			if (msg.Count > 1)
				log(String.Join(",\n\t", msg.ToArray()));

			return foundError;
		}

		private static void checkDockingJoint(List<string> msg, ModuleDockingNode node, PartJoint joint, bool isSameVessel)
		{
			if (!joint)
				return;
			bool valid = true;
			if (!joint.Host) {
				msg.Add("null host");
				valid = false;
			}
			if (!joint.Target) {
				msg.Add("null target");
				valid = false;
			}
			if (!valid)
				return;
			ModuleDockingNode other = node.getDockedNode(false);
			if (!other) {
				msg.Add("no other");
				return;
			}
			ModuleDockingNode host, target;
			if (node.part == joint.Host && other.part == joint.Target) {
				host = node;
				target = other;
			} else if (node.part == joint.Target && other.part == joint.Host) {
				host = other;
				target = node;
			} else {
				msg.Add("unrelated joint " + joint.info());
				return;
			}

			if (isSameVessel) {
				ModuleDockingNode child =
					host.part.parent == target.part ? host :
					target.part.parent == host.part ? target :
					null;
				if (child)
					msg.Add("should use tree joint " + child.part.attachJoint.info());
			}

			string label = "\"" + host.state + "\">\"" + target.state + "\""
				+ (isSameVessel ? ".isSameVessel" : ".isTree");
			JointState s = JointState.find(host, target, isSameVessel);
			if (s != null && s.fixer != null) {
				log(node.stateInfo(), ": fixing docking couple " + label);
				s.fixer(host, target);
				s = JointState.find(host, target, isSameVessel);
				if (s != null && s.fixer != null)
					s = null;
			}

			if (s == null)
				msg.Add("unallowed couple state " + label);
		}

		public static string stateInfo(this ModuleDockingNode node)
		{
			if (!node)
				return "null-node";
			if (!node.part)
				return "null-part";
			string ret = "MDN@" + node.part.flightID
				+ "_" + node.part.bareName()
				+ "<" + (node.part.parent ? node.part.parent.flightID : 0)
				+ ">" + node.dockedPartUId
				+ ":\"" + node.state + "\"";
			if (node.sameVesselDockJoint)
				ret += ":svdj=" + node.sameVesselDockJoint.GetInstanceID();
			PartJoint dj = node.getDockingJoint(out bool dsv, false);
			ret += ":dj=" + dj.info() + (dsv ? ":dsv" : "");
			return ret;
		}

		private static string info(this PartJoint j)
		{
			string ret = "PJ" + "[";
			if (j) {
				ret += j.GetInstanceID();
				ret += ":";
				ret += (j.Host ? j.Host.flightID : 0);
				ret += new string('>', j.joints.Count);
				ret += (j.Target ? j.Target.flightID : 0);
			} else {
				ret += "0";
			}
			ret += "]";
			return ret;
		}

		public static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(msg1, msg2);
		}
	}
}
