// --- Navbar Scroll Effect ---
window.addEventListener('scroll', function () {
    const navbar = document.querySelector('.premium-navbar');
    if (navbar) {
        if (window.scrollY > 50) {
            navbar.classList.add('bg-dark-scrolled', 'shadow-lg');
            navbar.style.padding = '10px 0';
            navbar.style.backgroundColor = 'rgba(15, 23, 42, 0.95)'; // Darker opacity
        } else {
            navbar.classList.remove('bg-dark-scrolled', 'shadow-lg');
            navbar.style.padding = '15px 0';
            navbar.style.backgroundColor = '#0F172A'; // Original Dark
        }
    }
});

// --- Language Toggle Logic ---
function toggleLanguage() {
    // Check current direction
    const htmlTag = document.documentElement;
    const currentLang = htmlTag.getAttribute('lang');

    if (currentLang === 'ar') {
        // Switch to English (Simulation)
        // In real app: window.location.href = '/Home/SetLanguage?culture=en-US';
        alert('Switching to English interface... (Demo Only)');
    } else {
        // Switch to Arabic
        // In real app: window.location.href = '/Home/SetLanguage?culture=ar-SA';
        alert('Switching to Arabic interface... (Demo Only)');
    }
}

// --- Smooth Scroll for Anchor Links ---
document.querySelectorAll('a[href^="#"]').forEach(anchor => {
    anchor.addEventListener('click', function (e) {
        e.preventDefault();
        const target = document.querySelector(this.getAttribute('href'));
        if (target) {
            target.scrollIntoView({
                behavior: 'smooth'
            });
        }
    });
});

// --- Simple Client Slider (Fallback if CSS animation isn't enough) ---
// Note: The CSS-only infinite scroll in site.css is more performant,
// but this adds drag functionality if needed later.