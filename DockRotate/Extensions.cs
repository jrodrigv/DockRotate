using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DockRotate
{
	public static class Extensions
	{
		/******** logging ********/

		private static int framelast = 0;
		private static string msg1last = "";

		public static bool log(string msg1, string msg2 = "")
		{
			int now = Time.frameCount;
			if (msg2 == "") {
				msg1last = "";
			} else {
				if (msg1 == msg1last && framelast == now) {
					msg1 = "    ... ";
				} else {
					framelast = now;
					msg1last = msg1;
				}
			}
			Debug.Log("[DR:"
#if DEBUG
				+ "d:"
#endif
				+ now + "] " + msg1 + msg2);
			return true;
		}

		/******** Camera utilities ********/

		public static string desc(this Camera c)
		{
			if (!c)
				return "null";
			return c.name + "(" + c.cameraType + ") @ " + c.gameObject;
		}

		/******** Vessel utilities ********/


		public static string desc(this Vessel v, bool bare = false)
		{
			uint id = (v && v.rootPart) ? v.rootPart.flightID : 0;
			string name = v ? v.name : "no-vessel";
			return (bare ? "" : "V:" + v.GetInstanceID() + ":") + id + ":" + name.Replace(' ', '_');
		}

		private static bool KJRNextInitDone = false;
		private static Type KJRNextManagerType = null;
		private static MethodInfo KJRNextCycleAllAutoStrutMethod = null;

		public static void KJRNextCycleAllAutoStrut(this Vessel v)
		{
			log(v.desc(), ".KJRNextCycleAllAutoStrut()");

			const string pref = "KJRNext init";

			if (!KJRNextInitDone) {
				KJRNextInitDone = true;
				AssemblyLoader.loadedAssemblies.TypeOperation(t => {
					if (t != null && t.FullName == "KerbalJointReinforcement.KJRManager") {
						KJRNextManagerType = t;
						if (KJRNextManagerType != null) {
							log(pref, ": found type " + KJRNextManagerType);
							KJRNextCycleAllAutoStrutMethod = KJRNextManagerType.GetMethod("CycleAllAutoStrut");
							if (KJRNextCycleAllAutoStrutMethod != null)
								log(pref, ": found method " + KJRNextCycleAllAutoStrutMethod);
						}
					}
				});
			}

			if (KJRNextManagerType != null && KJRNextCycleAllAutoStrutMethod != null) {
				object kjrnm = FlightGlobals.FindObjectOfType(KJRNextManagerType);
				if (kjrnm != null) {
					log(pref, ": found object " + kjrnm);
					KJRNextCycleAllAutoStrutMethod.Invoke(kjrnm, new object[] { v });
				}
			}
		}

		/******** Part utilities ********/

		public static string desc(this Part part, bool bare = false)
		{
			if (!part)
				return "null";
			string id = part.flightID > 0 ? part.flightID.ToString() : "I" + part.GetInstanceID();
			ModuleDockingNode mdn = bare ? null : part.FindModuleImplementing<ModuleDockingNode>();
			ModuleBaseRotate mbr = bare ? null : part.FindModuleImplementing<ModuleBaseRotate>();
			return (bare ? "" : "P:") + part.bareName() + ":" + id
				+ (mdn ? ":\"" + mdn.state + "\"" : "")
				+ (mbr ? ":" + mbr.nodeRole : "");
		}

		public static string bareName(this Part part)
		{
			if (!part)
				return "null";
			int s = part.name.IndexOf(' ');
			return s > 1 ? part.name.Remove(s) : part.name;
		}

		public static AttachNode[] allAttachNodes(this Part part)
		{
			List<AttachNode> ret = new List<AttachNode>();
			if (part.srfAttachNode != null)
				ret.Add(part.srfAttachNode);
			ret.AddRange(part.attachNodes);
			return ret.ToArray();
		}

		/******** Physics Activation utilities ********/

		public static bool hasPhysics(this Part part)
		{
			bool ret = (part.physicalSignificance == Part.PhysicalSignificance.FULL);
			if (ret != part.rb) {
				log(part.desc(), ": hasPhysics() Rigidbody incoherency: "
					+ part.physicalSignificance + ", " + (part.rb ? "rb ok" : "rb null"));
				ret = part.rb;
			}
			return ret;
		}

		public static bool forcePhysics(this Part part)
		{
			if (!part || part.hasPhysics())
				return false;

			log(part.desc(), ": calling PromoteToPhysicalPart(), "
				+ part.physicalSignificance + ", " + part.PhysicsSignificance);
			part.PromoteToPhysicalPart();
			log(part.desc(), ": called PromoteToPhysicalPart(), "
				+ part.physicalSignificance + ", " + part.PhysicsSignificance);
			if (part.parent) {
				if (part.attachJoint) {
					log(part.desc(), ": parent joint exists already: " + part.attachJoint.desc());
				} else {
					AttachNode nodeHere = part.FindAttachNodeByPart(part.parent);
					AttachNode nodeParent = part.parent.FindAttachNodeByPart(part);
					AttachModes m = (nodeHere != null && nodeParent != null) ?
						AttachModes.STACK : AttachModes.SRF_ATTACH;
					part.CreateAttachJoint(m);
					log(part.desc(), ": created joint " + m + " " + part.attachJoint.desc());
				}
			}

			return true;
		}

		/******** ModuleDockingNode utilities ********/

		public static ModuleDockingNode getDockedNode(this ModuleDockingNode node, bool verbose = false)
		{
			string label = node.part.desc();
			ModuleDockingNode other = node.otherNode;
			if (other) {
				if (verbose)
					log(label, ".getDockedNode(): other is " + other.part.desc());
			}
			if (!other && node.dockedPartUId > 0) {
				other = node.FindOtherNode();
				if (other && verbose)
					log(label, ".getDockedNode(): other found " + other.part.desc());
			}
			if (!other && verbose)
				log(label, ".getDockedNode(): no other, id = " + node.dockedPartUId);
			return other;
		}

		public static PartJoint getDockingJoint(this ModuleDockingNode node, out bool isSameVessel, bool verbose)
		{
			isSameVessel = false;

			ModuleDockingNode other = node.getDockedNode(verbose);
			if (!other)
				return null;

			PartJoint ret = node.sameVesselDockJoint;
			if (ret && ret.Target == other.part) {
				if (verbose)
					log(node.part.desc(), ".getDockingJoint(): to same vessel " + ret.desc());
				isSameVessel = true;
				return ret;
			}

			ret = other.sameVesselDockJoint;
			if (ret && ret.Target == node.part) {
				if (verbose)
					log(node.part.desc(), ".getDockingJoint(): from same vessel " + ret.desc());
				isSameVessel = true;
				return ret;
			}

			if (node.part.parent == other.part) {
				ret = node.part.attachJoint;
				if (verbose)
					log(node.part.desc(), ".getDockingJoint(): to parent " + ret.desc());
				return ret;
			}

			for (int i = 0; i < node.part.children.Count; i++) {
				Part child = node.part.children[i];
				if (child == other.part) {
					ret = child.attachJoint;
					if (verbose)
						log(node.part.desc(), ".getDockingJoint(): to child " + ret.desc());
					return ret;
				}
			}

			if (verbose)
				log(node.part.desc(), ".findMovingJoint(): nothing");
			return null;
		}

		public static ModuleDockRotate getDockRotate(this ModuleDockingNode node)
		{
			if (!node.part)
				return null;
			List<ModuleDockRotate> dr = node.part.FindModulesImplementing<ModuleDockRotate>();
			for (int i = 0; i < dr.Count; i++)
				if (dr[i].getDockingNode() == node)
					return dr[i];
			return null;
		}

		public static bool matchType(this ModuleDockingNode node, ModuleDockingNode other)
		{
			fillNodeTypes(node);
			fillNodeTypes(other);
			return node.nodeTypes.Overlaps(other.nodeTypes);
		}

		private static void fillNodeTypes(this ModuleDockingNode node)
		{
			// this fills nodeTypes, sometimes empty in editor
			if (node.nodeTypes != null && node.nodeTypes.Count > 0)
				return;
			log(node.part.desc(), ".fillNodeTypes(): fill with \"" + node.nodeType + "\"");
			if (node.nodeTypes == null)
				node.nodeTypes = new HashSet<string>();
			string[] types = node.nodeType.Split(',');
			for (int i = 0; i < types.Length; i++) {
				string type = types[i].Trim();
				if (type == "")
					continue;
				log(node.part.desc(), ".fillNodeTypes(): adding \"" + type + "\" [" + i + "]");
				node.nodeTypes.Add(type);
			}
		}

		/******** AttachNode utilities ********/

		public static AttachNode getConnectedNode(this AttachNode node, bool verbose)
		{
			if (verbose)
				log(node.desc(), ".getConnectedNode()");

			if (node == null || !node.owner)
				return null;

			AttachNode fon = node.FindOpposingNode();
			if (fon != null) {
				if (verbose)
					log(node.desc(), ".getConnectedNode(): FindOpposingNode() finds " + fon.desc());
				return fon;
			}

			List<Part> neighbours = new List<Part>();
			if (node.attachedPart) {
				neighbours.Add(node.attachedPart);
			} else {
				if (node.owner.parent)
					neighbours.Add(node.owner.parent);
				neighbours.AddRange(node.owner.children);
			}
			if (verbose)
				log(node.desc(), ".getConnectedNode(): " + node.owner.desc()
					+ " has " + neighbours.Count + " neighbours");

			AttachNode closest = null;
			float dist = 0f;
			for (int i = 0; i < neighbours.Count; i++) {
				if (neighbours[i] == null)
					continue;
				List<AttachNode> n = neighbours[i].attachNodes;
				if (verbose)
					log(node.desc(), ".getConnectedNode(): " + neighbours[i] + " has " + n.Count + " nodes");

				for (int j = 0; j < n.Count; j++) {
					float d = node.distFrom(n[j]);
					if (verbose)
						log(node.desc(), ".getConnectedNode(): " + n[j].desc() + " at " + d);
					if (d < dist || closest == null) {
						closest = n[j];
						dist = d;
					}
				}
			}
			if (verbose)
				log(node.desc(), ".getConnectedNode(): found " + closest.desc() + " at " + dist);

			if (closest == null || dist > 1e-2f)
				return null;

			return closest;
		}

		public static float distFrom(this AttachNode node, AttachNode other)
		{
			if (node == null || other == null || !node.owner || !other.owner)
				return 9e9f;
			Vector3 otherPos = other.position.Tp(other.owner.T(), node.owner.T());
			return (otherPos - node.position).magnitude;
		}

		public static string desc(this AttachNode n, bool bare = false)
		{
			if (n == null)
				return "null";
			return (bare ? "" : "AN:") + n.id + ":" + n.size
				+ ":" + n.owner.desc(true)
				+ ":" + (n.attachedPart ? n.attachedPart.desc(true) : "I" + n.attachedPartId);
		}

		/******** PartJoint utilities ********/

		public static bool isOffTree(this PartJoint j)
		{
			if (!j || !j.Host || !j.Target)
				return true;
			if (j == j.Host.attachJoint)
				return false;
			if (j.Host.parent == j.Target)
				log(j.desc(), ".isOffTree(): *** WARNING *** true at parent test");
			return true;
		}

		public static string desc(this PartJoint j, bool bare = false)
		{
			if (!j)
				return "null";
			string host = j.Host.desc(true) + (j.Child == j.Host ? "" : "/" + j.Child.desc(true));
			string target = j.Target.desc(true) + (j.Parent == j.Target ? "" : "/" + j.Parent.desc(true));
			int n = j.joints.Count;
			return (bare ? "" : "PJ:" + j.GetInstanceID() + ":") + host + new string('>', n) + target;
		}

		public static void dump(this PartJoint j)
		{
			log("PartJoint " + j.desc());
			log("jAxes: " + j.Axis.desc() + " " + j.SecAxis.desc());
			log("jAxes(rb): " + j.Axis.Td(j.Host.T(), j.Target.rb.T()).desc()
				+ ", " + j.SecAxis.Td(j.Host.T(), j.Target.rb.T()).desc());
			log("jAnchors: " + j.HostAnchor.desc() + " " + j.TgtAnchor.desc());

			for (int i = 0; i < j.joints.Count; i++) {
				log("ConfigurableJoint[" + i + "]:");
				j.joints[i].dump();
			}
		}

		/******** ConfigurableJoint utilities ********/

		public static void reconfigureForRotation(this ConfigurableJoint joint)
		{
			ConfigurableJointMotion f = ConfigurableJointMotion.Free;
			joint.angularXMotion = f;
			joint.angularYMotion = f;
			joint.angularZMotion = f;
			joint.xMotion = f;
			joint.yMotion = f;
			joint.zMotion = f;
		}

		public static void dump(this ConfigurableJoint j)
		{
			log("  Link: " + j.gameObject + " to " + j.connectedBody);
			log("  Axes: " + j.axis.desc() + ", " + j.secondaryAxis.desc());
			log("  Axes(rb): " + j.axis.Td(j.T(), j.connectedBody.T()).desc()
				+ ", " + j.secondaryAxis.Td(j.T(), j.connectedBody.T()).desc());

			log("  Anchors: " + j.anchor.desc()
				+ " -> " + j.connectedAnchor.desc()
				+ " [" + j.connectedAnchor.Tp(j.connectedBody.T(), j.T()).desc() + "]");

			log("  Tgt: " + j.targetPosition.desc() + ", " + j.targetRotation.desc());

			/*
			log("  angX: " + desc(j.angularXMotion, j.angularXDrive, j.lowAngularXLimit, j.angularXLimitSpring));
			log("  angY: " + desc(j.angularYMotion, j.angularYZDrive, j.angularYLimit, j.angularYZLimitSpring));
			log("  angZ: " + desc(j.angularZMotion, j.angularYZDrive, j.angularZLimit, j.angularYZLimitSpring));
			log("  linX: " + desc(j.xMotion, j.xDrive, j.linearLimit, j.linearLimitSpring));
			log("  linY: " + desc(j.yMotion, j.yDrive, j.linearLimit, j.linearLimitSpring));
			log("  linZ: " + desc(j.zMotion, j.zDrive, j.linearLimit, j.linearLimitSpring));

			log("  proj: " + j.projectionMode + " ang=" + j.projectionAngle + " dst=" + j.projectionDistance);
			*/
		}

		public static string desc(ConfigurableJointMotion mot, JointDrive drv, SoftJointLimit lim, SoftJointLimitSpring spr)
		{
			return mot.ToString() + " " + drv.desc() + " " + lim.desc() + " " + spr.desc();
		}

		public static string desc(this JointDrive drive)
		{
			return "drv(frc=" + drive.maximumForce
				+ " spr=" + drive.positionSpring
				+ " dmp=" + drive.positionDamper
				+ ")";
		}

		public static string desc(this SoftJointLimit limit)
		{
			return "lim(lim=" + limit.limit
				+ " bnc=" + limit.bounciness
				+ " dst=" + limit.contactDistance
				+ ")";
		}

		public static string desc(this SoftJointLimitSpring spring)
		{
			return "spr(spr=" + spring.spring
				+ " dmp=" + spring.damper
				+ ")";
		}

		/******** action utilities ********/

		public static string desc(this KSPActionParam p)
		{
			return "[" + p.group + ", " + p.type + ", " + p.Cooldown + "]";
		}

		/******** float utilities ********/

		public static bool isZero(this float n)
		{
			return Mathf.Approximately(n, 0f);
		}

		/******** Vector3 utilities ********/

		public static Vector3 findUp(this Vector3 axis)
		{
			Vector3 up1 = Vector3.ProjectOnPlane(Vector3.up, axis);
			Vector3 up2 = Vector3.ProjectOnPlane(Vector3.forward, axis);
			return (up1.magnitude >= up2.magnitude ? up1 : up2).normalized;
		}

		public static Quaternion rotation(this Vector3 axis, float angle)
		{
			return Quaternion.AngleAxis(angle, axis);
		}

		public static float axisSignedAngle(this Vector3 axis, Vector3 v1, Vector3 v2)
		{
			v1 = Vector3.ProjectOnPlane(v1, axis).normalized;
			v2 = Vector3.ProjectOnPlane(v2, axis).normalized;
			float angle = Vector3.Angle(v1, v2);
			float s = Vector3.Dot(axis, Vector3.Cross(v1, v2));
			return (s < 0) ? -angle : angle;
		}

		public static string desc(this Vector3 v)
		{
			return v.ToString(v == Vector3.zero ? "F0" : "F2");
		}

		public static string ddesc(this Vector3 v, Part p)
		{
			string ret = v.desc();
			if (p && p.vessel.rootPart) {
				ret += " VSL" + v.Td(p.T(), p.vessel.rootPart.T()).desc();
			} else {
				ret += " (no vessel)";
			}
			return ret;
		}

		public static string pdesc(this Vector3 v, Part p)
		{
			string ret = v.desc();
			if (p && p.vessel.rootPart) {
				ret += " VSL" + v.Tp(p.T(), p.vessel.rootPart.T()).desc();
			} else {
				ret += " (no vessel)";
			}
			return ret;
		}

		/******** Quaternion utilities ********/

		public static Quaternion inverse(this Quaternion q)
		{
			return Quaternion.Inverse(q);
		}

		public static string desc(this Quaternion q)
		{
			float angle;
			Vector3 axis;
			q.ToAngleAxis(out angle, out axis);
			bool isIdentity = angle.isZero();
			return angle.ToString(isIdentity ? "F0" : "F1") + "\u00b0"
				+ (isIdentity ? Vector3.zero : axis).desc();
		}
	}
}

