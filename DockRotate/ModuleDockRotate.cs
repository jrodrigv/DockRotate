using System;
using UnityEngine;
using KSP.Localization;
using KSP.UI.Screens.DebugToolbar;

namespace DockRotate
{
	public class ModuleDockRotate: ModuleBaseRotate
	{
		/*

			docking node states:

			* PreAttached
			* Docked (docker/same vessel/dockee) - (docker) and (same vessel) are coupled with (dockee)
			* Ready
			* Disengage
			* Acquire
			* Acquire (dockee)

		*/

		private ModuleDockingNode dockingNode;

		public ModuleDockingNode getDockingNode()
		{
			return dockingNode;
		}

		[KSPEvent(
			guiActive = true,
#if DEBUG
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
#else
			groupName = GROUP,
			groupDisplayName = GROUP,
#endif
			groupStartCollapsed = true,
			guiActiveUncommand = true
		)]
		public void CheckDockingState()
		{
			log(desc(), ": DOCKING STATE CHECK");
			if (dockingNode) {
				dockingNode.isBadNode(true);
				ModuleDockingNode other = dockingNode.getDockedNode();
				if (other) {
					log(desc(), ": DOCKING STATE CHECK - OTHER");
					other.isBadNode(true);
				}
			}
		}

		public void showCheckDockingState(bool active)
		{
			BaseEvent evt = Events["CheckDockingState"];
			if (evt != null) {
				evt.guiActive = active;
				// log(desc(), ".showCheckDockingState(" + active + "): done");
			} else {
				log(desc(), ".showCheckDockingState(" + active + "): can't find event");
			}
		}

		protected override void fillInfo()
		{
			storedModuleDisplayName = Localizer.Format("#DCKROT_port_displayname");
			storedInfo = Localizer.Format("#DCKROT_port_info");
		}

		protected override AttachNode findMovingNodeInEditor(out Part otherPart, bool verbose)
		{
			otherPart = null;
			if (!dockingNode || dockingNode.referenceNode == null)
				return null;
			if (verbose)
				log(desc(), ".findMovingNodeInEditor(): referenceNode = " + dockingNode.referenceNode.desc());
			AttachNode otherNode = dockingNode.referenceNode.getConnectedNode(verboseEvents);
			if (verbose)
				log(desc(), ".findMovingNodeInEditor(): otherNode = " + otherNode.desc());
			if (otherNode == null)
				return null;
			otherPart = otherNode.owner;
			if (verbose)
				log(desc(), ".findMovingNodeInEditor(): otherPart = " + otherPart.desc());
			if (!otherPart)
				return null;
			ModuleDockingNode otherDockingNode = otherPart.FindModuleImplementing<ModuleDockingNode>();
			if (verbose)
				log(desc(), ".findMovingNodeInEditor(): otherDockingNode = "
					+ (otherDockingNode ? otherDockingNode.part.desc() : "null"));
			if (!otherDockingNode)
				return null;
			if (verbose)
				log(desc(), ".findMovingNodeInEditor(): otherDockingNode.referenceNode = "
					+ otherDockingNode.referenceNode.desc());
			if (otherDockingNode.referenceNode == null)
				return null;
			if (!otherDockingNode.matchType(dockingNode)) {
				if (verbose)
					log(desc(), ".findMovingNodeInEditor(): mismatched node types "
						+ dockingNode.nodeType + " != " + otherDockingNode.nodeType);
				return null;
			}
			if (verbose)
				log(desc(), ".findMovingNodeInEditor(): node test is "
					+ (otherDockingNode.referenceNode.FindOpposingNode() == dockingNode.referenceNode));

			return dockingNode.referenceNode;
		}

		protected override bool setupLocalAxis(StartState state)
		{
			dockingNode = part.FindModuleImplementing<ModuleDockingNode>();

			if (!dockingNode) {
				log(desc(), ".setupLocalAxis(" + state + "): no docking node");
				return false;
			}

			partNodePos = Vector3.zero.Tp(dockingNode.T(), part.T());
			partNodeAxis = Vector3.forward.Td(dockingNode.T(), part.T());
			if (verboseEvents)
				log(desc(), ".setupLocalAxis(" + state + ") done: "
					+ partNodeAxis + "@" + partNodePos);
			return true;
		}

		protected override PartJoint findMovingJoint(bool verbose)
		{
			if (!dockingNode || !dockingNode.part) {
				if (verbose)
					log(desc(), ".findMovingJoint(): no docking node");
				return null;
			}

			ModuleDockingNode other = dockingNode.getDockedNode(verbose);
			if (!other || !other.part) {
				if (verbose)
					log(desc(), ".findMovingJoint(): no other, id = " + dockingNode.dockedPartUId);
				return null;
			}

			if (!dockingNode.matchType(other)) {
				if (verbose)
					log(desc(), ".findMovingJoint(): mismatched node types");
				return null;
			}

			ModuleBaseRotate otherModule = other.part.FindModuleImplementing<ModuleBaseRotate>();
			if (otherModule) {
				if (!smartAutoStruts && otherModule.smartAutoStruts) {
					smartAutoStruts = true;
					log(desc(), ".findMovingJoint(): smartAutoStruts activated by " + otherModule.desc());
				}
			}

			return dockingNode.getDockingJoint(out bool isSameVessel, verbose);
		}

		private static bool consoleSetupDone = false;

		protected override void doSetup()
		{
			showCheckDockingState(false);

			base.doSetup();

			if (!consoleSetupDone) {
				consoleSetupDone = true;
				DebugScreenConsole.AddConsoleCommand("dr", consoleCommand, "DockRotate commands");
			}

			if (hasJointMotion && jointMotion.joint.Host == part && !frozenFlag) {
				float snap = autoSnapStep();
				if (verboseEvents)
					log(desc(), ".autoSnapStep() = " + snap);
				ModuleDockRotate other = jointMotion.joint.Target.FindModuleImplementing<ModuleDockRotate>();
				if (other) {
					float otherSnap = other.autoSnapStep();
					if (verboseEvents)
						log(other.desc(), ".autoSnapStep() = " + otherSnap);
					if (otherSnap > 0f && (snap.isZero() || otherSnap < snap))
						snap = otherSnap;
				}
				if (!snap.isZero()) {
					if (verboseEvents)
						log(jointMotion.desc(), ": autosnap at " + snap);
					enqueueFrozenRotation(jointMotion.angleToSnap(snap), 5f);
				}
			}
		}

#if DEBUG
		public override void dumpExtra()
		{
			string d = desc();
			if (dockingNode) {
				log(d, ": attachJoint: " + part.attachJoint.desc());
				log(d, ": dockedPartUId: " + dockingNode.dockedPartUId);
				log(d, ": dockingNode state: \"" + dockingNode.state + "\"");
				log(d, ": sameVesselDockingJoint: " + dockingNode.sameVesselDockJoint.desc());
			} else {
				log(d, ": no dockingNode");
			}
		}
#endif

		private static char[] commandSeparators = { ' ', '\t' };

		public static void consoleCommand(string arg)
		{
			string[] args = arg.Split(commandSeparators, StringSplitOptions.RemoveEmptyEntries);
			Vessel v = FlightGlobals.ActiveVessel;
			if (!HighLogic.LoadedSceneIsFlight) {
				log("not in flight mode");
			} else if (!v) {
				log("no active vessel");
			} else if (args.Length == 1 && args[0] == "check") {
				VesselMotionManager vmm = VesselMotionManager.get(v);
				if (vmm)
					vmm.scheduleDockingStatesCheck(0, true);
			} else {
				log("illegal command");
			}
			// log("CMD END");
		}

		private float autoSnapStep()
		{
			if (!hasJointMotion || !dockingNode)
				return 0f;

			float step = 0f;
			string source = "no source";
			if (dockingNode.snapRotation && dockingNode.snapOffset > 0.01f) {
				step = dockingNode.snapOffset;
				source = "snapOffset";
			} else if (autoSnap && rotationEnabled && rotationStep > 0.01f) {
				step = rotationStep;
				source = "rotationStep";
			}
			if (verboseEvents)
				log(desc(), ".autoSnapStep() = " + step + " from " + source);
			if (step >= 360f)
				step = 0f;
			return step;
		}

		public override string descPrefix()
		{
			return "MDR";
		}
	}
}
