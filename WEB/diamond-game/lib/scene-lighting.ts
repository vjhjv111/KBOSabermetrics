import * as THREE from "three";

/** A small stadium lighting rig; no post-processing passes or remote HDR assets. */
export function stadiumLighting(renderer: THREE.WebGLRenderer, scene: THREE.Scene, compact: boolean) {
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.02;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFShadowMap;

  scene.add(new THREE.HemisphereLight("#d9e5f4", "#77604a", 1.05));
  const key = new THREE.DirectionalLight("#fff0dc", 2.7);
  key.castShadow = true;
  key.shadow.mapSize.setScalar(compact ? 512 : 1024);
  key.shadow.camera.left = key.shadow.camera.bottom = -3.4;
  key.shadow.camera.right = key.shadow.camera.top = 3.4;
  key.shadow.camera.near = .5;
  key.shadow.camera.far = 22;
  key.shadow.bias = -.00025;
  key.shadow.normalBias = .018;
  key.shadow.radius = 2;
  scene.add(key, key.target);
  const fill = new THREE.DirectionalLight("#c5dbf4", .65);
  fill.position.set(7, 5, 7);
  scene.add(fill);
  const rim = new THREE.DirectionalLight("#fff6e7", 1.25);
  rim.position.set(3, 7, -24);
  scene.add(rim);

  // Broad floodlight reflections give helmets and leather their own material response.
  const surroundings = new THREE.Scene();
  surroundings.background = new THREE.Color("#8e9dac");
  const cards: THREE.Mesh[] = [];
  for (const [x,y,z,sx,sy,intensity] of [[-4,7,3,4,3,5],[6,5,-4,3,2,3],[0,8,-1,5,3,2.5]]) {
    const card = new THREE.Mesh(new THREE.PlaneGeometry(sx,sy), new THREE.MeshBasicMaterial({color: new THREE.Color().setScalar(intensity), side: THREE.DoubleSide}));
    card.position.set(x,y,z); card.lookAt(0,1,0); surroundings.add(card); cards.push(card);
  }
  const generator = new THREE.PMREMGenerator(renderer);
  const reflection = generator.fromScene(surroundings, .06, .1, 30);
  scene.environment = reflection.texture;
  scene.environmentIntensity = .32;
  generator.dispose();
  for (const card of cards) { card.geometry.dispose(); (card.material as THREE.Material).dispose(); }

  const ground = new THREE.Mesh(new THREE.PlaneGeometry(8,8), new THREE.ShadowMaterial({color: "#182022", opacity: .28, depthWrite: false}));
  ground.rotation.x = -Math.PI/2;
  ground.position.y = .002;
  ground.receiveShadow = true;
  scene.add(ground);
  const focus = (side: "batter" | "pitcher") => {
    const z = side === "pitcher" ? -18.44 : 0;
    key.position.set(-4,9,z+4);
    key.target.position.set(0,.85,z);
    ground.position.z = z;
  };
  focus("batter");
  return {focus, dispose: () => { reflection.dispose(); key.shadow.dispose(); scene.environment = null; }};
}
