/*earthRotation.js*/
// ============================

let earth, scene, camera, renderer;
let rotationSpeed = 0.01; // initial speed
let frameId;

export function initEarth() {
    // Canvas Recovery
    const canvas = document.getElementById("rotatingEarth");

    // Initializing the Three.js scene
    scene = new THREE.Scene();

    // Camera perspective
    camera = new THREE.PerspectiveCamera(45, canvas.clientWidth / canvas.clientHeight, 0.1, 1000);
    camera.position.z = 3;

    // Renderer with transparency for overlay
    renderer = new THREE.WebGLRenderer({ canvas: canvas, alpha: true, antialias: true });
    renderer.setSize(canvas.clientWidth, canvas.clientHeight);

    // Lights
    const ambientLight = new THREE.AmbientLight(0xffffff, 0.7);  // diffused light
    scene.add(ambientLight);

    const directionalLight = new THREE.DirectionalLight(0xffffff, 0.8);
    directionalLight.position.set(5, 3, 5);
    scene.add(directionalLight);

    // Loading Earth texture
    const loader = new THREE.TextureLoader();
    loader.load("/images/earth_texture.jpg", (texture) => {
        const geometry = new THREE.SphereGeometry(1, 64, 64);
        const material = new THREE.MeshPhongMaterial({
            map: texture,
            shininess: 30, // for a luxurious shiny effect
            specular: new THREE.Color('grey')
        });

        earth = new THREE.Mesh(geometry, material);
        scene.add(earth);

        animate();
    });

    // Speed control management by slider
    const speedControl = document.getElementById("speedRange");
    speedControl.addEventListener("input", (e) => {
        rotationSpeed = parseFloat(e.target.value);
    });

    // Resizing Adaptation (optional)
    window.addEventListener('resize', onWindowResize);
}

function animate() {
    frameId = requestAnimationFrame(animate);
    if (earth) {
        earth.rotation.y += rotationSpeed;
    }
    renderer.render(scene, camera);
}

function onWindowResize() {
    const canvas = document.getElementById("rotatingEarth");
    camera.aspect = canvas.clientWidth / canvas.clientHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(canvas.clientWidth, canvas.clientHeight);
}

// For possible cleanup (not mandatory)
export function disposeEarth() {
    cancelAnimationFrame(frameId);
    if (earth) {
        earth.geometry.dispose();
        earth.material.dispose();
        scene.remove(earth);
        earth = null;
    }
    renderer.dispose();
}

window.initEarth = initEarth;
window.disposeEarth = disposeEarth;















































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/