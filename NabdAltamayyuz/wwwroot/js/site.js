// --- Navbar Scroll Effect ---
document.addEventListener("DOMContentLoaded", function () {
    const navbar = document.querySelector('.premium-navbar');

    if (navbar) {
        window.addEventListener('scroll', function () {
            if (window.scrollY > 50) {
                navbar.classList.add('scrolled');
            } else {
                navbar.classList.remove('scrolled');
            }
        });
    }
    const isRtl = document.documentElement.getAttribute('dir') === 'rtl';

    flatpickr("input[type='date']", {
        dateFormat: "Y-m-d",
        locale: isRtl ? "ar" : "en",
        altInput: true,
        altFormat: "F j, Y", // e.g. January 1, 2024
        allowInput: true,
        disableMobile: "true", // Force custom calendar on mobile too
        theme: "dark" // Base theme, customized in CSS
    });

    // --- File Upload Validation (Max 5MB) ---
    const fileInputs = document.querySelectorAll('input[type="file"]');
    fileInputs.forEach(input => {
        input.addEventListener('change', function () {
            if (this.files && this.files[0]) {
                const fileSize = this.files[0].size / 1024 / 1024; // in MB
                if (fileSize > 5) {
                    alert('عذراً، حجم الملف كبير جداً. الحد الأقصى هو 5 ميجابايت.');
                    this.value = ''; // Clear input
                }
            }
        });
    });

    // --- Animation on Scroll ---
    const observerOptions = {
        root: null,
        rootMargin: '0px',
        threshold: 0.1
    };

    const observer = new IntersectionObserver((entries, observer) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.style.opacity = "1";
                entry.target.style.transform = "translateY(0)";
                observer.unobserve(entry.target);
            }
        });
    }, observerOptions);

    const animatedElements = document.querySelectorAll('.animate-up, .card-premium, .hover-lift');
    animatedElements.forEach((el) => {
        el.style.opacity = "0";
        el.style.transform = "translateY(20px)";
        el.style.transition = "opacity 0.6s ease-out, transform 0.6s ease-out";
        observer.observe(el);
    });

    // --- Bootstrap Tooltips ---
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'))
    var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl)
    });
});