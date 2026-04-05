VividSoul VRMA Safe Set

Target: current VividSoul Unity runtime
Authoring envelope: humanoid pose + restrained hips translation, no lookAt dependency, no expression dependency

Files:
01_idle_soft_10s.vrma -> Loop
02_click_feedback_2s.vrma -> ReturnToIdle / click action
11_pose_wave_friendly_4s.vrma -> PlayOnce
12_pose_nod_gentle_3s.vrma -> PlayOnce
13_pose_turn_showcase_6s.vrma -> HoldLastFrame
14_pose_breath_upperbody_8s.vrma -> Loop or HoldLastFrame

Action probes:
{'VS_11_Pose_Wave_Friendly_4s': {'upper_arm.R': (0.5236, 0.0, -0.1309), 'hand.R': (0.2129, 0.0, -0.1536), 'head': (-0.0489, 0.0, -0.1047), 'hips_loc': (0.0, 0.0, 0.0)}, 'VS_13_Pose_Turn_Showcase_6s': {'upper_arm.R': (-1.3614, 0.0, -0.0262), 'hand.R': (0.0384, 0.0, 0.0908), 'head': (-0.0489, 0.0, -0.3142), 'hips_loc': (0.0015, 0.0, 0.0)}, 'VS_02_Click_Feedback_2s': {'upper_arm.R': (-1.3247, 0.0, 0.0436), 'hand.R': (0.0812, 0.0, 0.0908), 'head': (-0.1117, 0.0, 0.0), 'hips_loc': (0.0, 0.0006, 0.0)}}