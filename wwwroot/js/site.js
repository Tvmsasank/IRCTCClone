// ================= SIGNALR CONNECTION =================

document.addEventListener("DOMContentLoaded", function () {

    if (typeof signalR === "undefined") {

        console.error("SignalR library not loaded");

        return;
    }

    const trainConnection = new signalR.HubConnectionBuilder()
        .withUrl("/trainHub")
        .withAutomaticReconnect()
        .build();

    trainConnection.start()
        .then(() => {

            console.log("TrainHub connected");
        })
        .catch(err => console.error("SignalR Error:", err));

    // ================= RECEIVE LIVE UPDATES =================

    trainConnection.on("AvailabilityUpdated", function (data) {

        console.log("Live availability update:", data);

        const trainId = data.trainId;
        const classId = data.classId;

        const panel = document.getElementById("panel-" + trainId);

        if (!panel) return;

        const card = panel.querySelector(
            `.class-card[data-class="${classId}"]`
        );

        if (!card) return;

        const statusEl = card.querySelector(".class-status");

        if (!statusEl) return;

        if (data.status === "AVAILABLE") {

            statusEl.innerText =
                "AVAILABLE - " + data.availableSeats;

            statusEl.style.color = "green";
        }

        else if (data.status === "RAC") {

            statusEl.innerText =
                "RAC - " + data.racCount;

            statusEl.style.color = "orange";
        }

        else if (data.status === "WL") {

            statusEl.innerText =
                "WL - " + data.wlCount;

            statusEl.style.color = "red";
        }

        else {

            statusEl.innerText = "NOT AVAILABLE";

            statusEl.style.color = "gray";
        }

        const timeEl = card.querySelector(".last-updated");

        if (timeEl) {
            timeEl.innerText = "Updated just now";
        }
    });

    // GLOBAL ACCESS
    window.trainConnection = trainConnection;
});
$(function () {
    ///////////////////////////////////////////////////////////////////////////
    // 2. CANCEL BOOKING CONFIRMATION
    ///////////////////////////////////////////////////////////////////////////
    $(document).on('submit', '.inline-cancel-form', function (e) {

        if (!confirm('Are you sure you want to cancel this booking?')) {
            e.preventDefault();
            return false;
        }

        $(this).find("button").prop("disabled", true).text("Cancelling...");
    });

    ///////////////////////////////////////////////////////////////////////////
    // 3. AUTOCOMPLETE
    ///////////////////////////////////////////////////////////////////////////
    function debounce(fn, delay) {
        let timer;
        return function () {
            const context = this;
            const args = arguments;
            clearTimeout(timer);
            timer = setTimeout(() => fn.apply(context, args), delay);
        };
    }

    function wireAutocomplete($input, $list) {
        function fetchStations(term, callback) {
            $.ajax({
                url: '/Train/GetStations',
                type: 'GET',
                data: { term: term },

                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                },

                success: function (data) {
                    callback(data || []);
                },
                error: function (xhr) {
                    if (xhr.status === 401) {
                        window.location.replace("/Account/Login");
                    }
                }
            });
        }

        function renderList(items) {

            $list.empty();

            const inputVal = $input.val().toLowerCase();

            if (!items.length) {

                // 🔥 SHOW JOURNEYS EVEN IF NO STATIONS
                let recent = JSON.parse(localStorage.getItem("recentJourneys")) || [];

                const filteredRecent = recent.filter(r =>
                    r.fromText.toLowerCase().includes(inputVal) ||
                    r.toText.toLowerCase().includes(inputVal)
                );

                if (filteredRecent.length > 0) {
                    $list.append('<div class="autocomplete-header">---- Journeys ----</div>');

                    filteredRecent.forEach(r => {
                        $list.append(
                            $('<div class="autocomplete-item recent-item">')
                                .text(`${r.fromText} → ${r.toText}`)
                                .data('fromId', r.fromId)
                                .data('toId', r.toId)
                                .data('type', 'recent')
                        );
                    });
                }

                // 🔥 STILL SHOW STATIONS HEADER (EMPTY STATE)
                $list.append('<div class="autocomplete-header">---- Stations ----</div>');

                // OPTIONAL (better UX)
                $list.append(
                    $('<div class="autocomplete-empty">')
                        .text('No stations found')
                );

                $list.show();
                return;
            }

            // SORT
            items.sort((a, b) => {

                const aCode = a.code.toLowerCase();
                const bCode = b.code.toLowerCase();

                if (aCode === inputVal) return -1;
                if (bCode === inputVal) return 1;

                if (aCode.startsWith(inputVal)) return -1;
                if (bCode.startsWith(inputVal)) return 1;

                if (a.name.toLowerCase().startsWith(inputVal)) return -1;
                if (b.name.toLowerCase().startsWith(inputVal)) return 1;

                return 0;
            });

            // ✅ TOP MATCH (FIXED)
            let strongMatches = [];

            if (inputVal.length === 1) {
                //Only exact code match for single letter
                strongMatches = items.filter(s =>
                    s.code.toLowerCase() === inputVal
                );
            }

            else if (inputVal.length > 1) {   // ✅ ONLY for multi-letter
                strongMatches = items.filter(s =>
                    s.code.toLowerCase().startsWith(inputVal) ||
                    s.name.toLowerCase().startsWith(inputVal)
                );
            }

            let topStation = null;

            if (strongMatches.length > 0) {
                topStation = strongMatches[0];
            }

            if (topStation) {
                const $top = $('<div class="autocomplete-item active top-item">')
                .html(`
                    <div class="station-main">${topStation.displayName}</div>
                    <div class="station-state">${topStation.state || ''}</div>
                `)
                .data('id', topStation.id)
                .data('type', 'station');

                $list.append($top);
            }

            // RECENT
            let recent = JSON.parse(localStorage.getItem("recentJourneys")) || [];

            const filteredRecent = recent.filter(r =>
                r.fromText.toLowerCase().includes(inputVal) ||
                r.toText.toLowerCase().includes(inputVal)
            );

            if (filteredRecent.length > 0) {
                $list.append('<div class="autocomplete-header">---- Journeys ----</div>');

                filteredRecent.forEach(r => {
                    $list.append(
                        $('<div class="autocomplete-item recent-item">')
                            .text(`${r.fromText} → ${r.toText}`)
                            .data('fromId', r.fromId)
                            .data('toId', r.toId)
                            .data('type', 'recent')
                    );
                });
            }

            // REMAINING STATIONS ✅ FIXED
            const remainingStations = topStation
                ? items.filter(s => s.id !== topStation.id)
                : items;

            if (remainingStations.length > 0) {

                $list.append('<div class="autocomplete-header">---- Stations ----</div>');

                remainingStations.forEach(s => {
                    $list.append(
                        $('<div class="autocomplete-item">')
                        .html(`
                            <div class="station-main">${s.displayName}</div>
                            <div class="station-state">${s.state || ''}</div>
                        `)
                        .data('id', s.id)
                        .data('type', 'station')
                    );
                });
            }

            $list.show();
            $list.scrollTop(0);
        }

        // Typing triggers search
        $input.on('input', debounce(function () {
            const term = $input.val().trim();
            if (!term) {
                $list.hide();
                if ($input.is('#fromStation')) $('#fromStationId').val('');
                if ($input.is('#toStation')) $('#toStationId').val('');
                return;
            }
            fetchStations(term, renderList);
        }, 200));

        // Selecting from dropdown (click)
        $list.on('click', '.autocomplete-item', function () {

            const type = $(this).data('type');

            // 🔥 RECENT SEARCH CLICK
            if (type === 'recent') {

                const fromId = $(this).data('fromId');
                const toId = $(this).data('toId');

                const text = $(this).text().split("→");

                $('#fromStation').val(text[0].trim());
                $('#toStation').val(text[1].trim());

                $('#fromStationId').val(fromId);
                $('#toStationId').val(toId);

                $list.hide();
                return;
            }

            // 🔥 NORMAL STATION
            selectItem($(this));
        });

        // Keyboard navigation
        $input.on('keydown', function (e) {

            const $items = $list.find('.autocomplete-item:visible');

            if (!$items.length) return;

            let index = $items.index($items.filter('.active'));

            if (e.key === 'ArrowDown') {
                e.preventDefault();

                index = (index + 1) % $items.length;

                $items.removeClass('active');
                $items.eq(index).addClass('active');

                scrollIntoView($items.eq(index));
            }

            else if (e.key === 'ArrowUp') {
                e.preventDefault();

                index = (index - 1 + $items.length) % $items.length;

                $items.removeClass('active');
                $items.eq(index).addClass('active');

                scrollIntoView($items.eq(index));
            }

            else if (e.key === 'Enter') {
                e.preventDefault();

                const $active = $items.filter('.active');

                if ($active.length) {
                    $active.click();
                }
            }
        });

        function scrollIntoView($item) {

            if (!$item || !$item.length) return; // safety

            const container = $item.parent();

            const itemTop = $item.position().top;
            const itemBottom = itemTop + $item.outerHeight();

            const scrollTop = container.scrollTop();
            const containerHeight = container.innerHeight();

            if (itemBottom > containerHeight) {
                container.scrollTop(scrollTop + (itemBottom - containerHeight));
            }
            else if (itemTop < 0) {
                container.scrollTop(scrollTop + itemTop);
            }
        }

        function selectItem($item) {
            const text = $item.find('.station-main').text().trim();
            const id = $item.data('id');

            $input.val(text);
            $input.data('selected', true);

            $list.hide();

            if ($input.is('#fromStation')) $('#fromStationId').val(id);
            if ($input.is('#toStation')) $('#toStationId').val(id);
        }

        // Hide dropdown when clicking outside
        $(document).on('click', function (e) {
            if (!$(e.target).closest($input).length && !$(e.target).closest($list).length) {
                $list.hide();
            }
        });

        $input.on('input', function () {

            // 🔥 reset selection flag
            $input.data('selected', false);

            // clear hidden id ONLY when typing
            if ($input.is('#fromStation')) $('#fromStationId').val('');
            if ($input.is('#toStation')) $('#toStationId').val('');
        });
    }

    // Initialize both inputs
    $(function () {

        if ($('#fromStation').length && $('#fromStationList').length) {
            wireAutocomplete($('#fromStation'), $('#fromStationList'));
        }

        if ($('#toStation').length && $('#toStationList').length) {
            wireAutocomplete($('#toStation'), $('#toStationList'));
        }

    });   

    ///////////////////////////////////////////////////////////////////////////
    // 4. SWAP STATION LOGIC
    ///////////////////////////////////////////////////////////////////////////

    $(document).off('click', '#swapStations').on('click', '#swapStations', function (e) {

        e.preventDefault();

        console.log("Swap clicked");

        const fromInput = $('#fromStation');
        const toInput = $('#toStation');

        const fromHidden = $('#fromStationId');
        const toHidden = $('#toStationId');

        // Store values FIRST
        const fromVal = fromInput.val();
        const toVal = toInput.val();

        const fromId = fromHidden.val();
        const toId = toHidden.val();

        // Swap AFTER small delay (bypass autocomplete override)
        setTimeout(() => {
            fromInput.val(toVal);
            toInput.val(fromVal);

            fromHidden.val(toId);
            toHidden.val(fromId);
        }, 10);
    });

    $(document).off('click', '#swapStationsNew').on('click', '#swapStationsNew', function (e) {

        e.preventDefault();

        console.log("Swap clicked");

        const fromInput = $('#fromStation');
        const toInput = $('#toStation');

        const fromHidden = $('#fromStationId');
        const toHidden = $('#toStationId');

        // Store values FIRST
        const fromVal = fromInput.val();
        const toVal = toInput.val();

        const fromId = fromHidden.val();
        const toId = toHidden.val();

        // Swap AFTER small delay (bypass autocomplete override)
        setTimeout(() => {
            fromInput.val(toVal);
            toInput.val(fromVal);

            fromHidden.val(toId);
            toHidden.val(fromId);
        }, 10);
    });

    $(document).off("submit", "#adminLoginForm")
    .on("submit", "#adminLoginForm", function (e) {

        const form = this;
        const btn = $(this).find("button[type='submit']");

        if (!form.checkValidity()) {
            e.preventDefault();
            e.stopImmediatePropagation();

            // 🔥 DEBUG (add this temporarily)
            console.log("Admin validation triggered");

            showValidationErrors(form);   // ✅ THIS SHOULD RUN

            return false;
        }

        btn.prop("disabled", true).text("Logging in...");
        showLoader();
    });

    const cancelCheckbox = document.getElementById("cancelTrainCheckbox");

    if (cancelCheckbox) {
        cancelCheckbox.addEventListener("change", function () {

            let dates = document.getElementById("selectedDates")?.value;

            if (this.checked && (!dates || dates.trim() === "")) {
                alert("Please select at least one date");
                this.checked = false;
            }
        });
    }

//    function saveRecentSearch(fromText, toText, fromId, toId) {

//        let recent = JSON.parse(localStorage.getItem("recentJourneys")) || [];

//        const newItem = {
//            fromText,
//            toText,
//            fromId,
//            toId
//        };

//        // remove duplicate
//        recent = recent.filter(x => !(x.fromId == fromId && x.toId == toId));

//        // add latest on top
//        recent.unshift(newItem);

//        // keep only 2
//        recent = recent.slice(0, 2);

//        localStorage.setItem("recentJourneys", JSON.stringify(recent));
//    }
});

window.saveRecentSearch = function (fromText, toText, fromId, toId) {

    let recent = JSON.parse(localStorage.getItem("recentJourneys")) || [];

    const newItem = {
        fromText,
        toText,
        fromId,
        toId
    };

    // remove duplicate
    recent = recent.filter(x => !(x.fromId == fromId && x.toId == toId));

    // add latest on top
    recent.unshift(newItem);

    // keep only 2
    recent = recent.slice(0, 2);

    localStorage.setItem("recentJourneys", JSON.stringify(recent));
};

/*-------------------------------------------------------------------------------------------*/
window.addEventListener("pageshow", function (event) {
    const navEntries = performance.getEntriesByType("navigation");
    const isBackForward = event.persisted || (navEntries.length > 0 && navEntries[0].type === "back_forward") || (window.performance && window.performance.navigation && window.performance.navigation.type === 2);
    if (isBackForward) {
        sessionStorage.setItem("backButtonReload", "true");
        window.location.reload();
    }
});

/*------------------------------------------------------------------------------------------*/
// GLOBAL LOADER FUNCTIONS
function showLoader() {
    const loader = document.getElementById("globalLoader");
    if (loader) loader.style.display = "flex";
}

/*------------------------------------------------------------------------------------------*/
// GLOBAL LOADER FUNCTIONS
function hideLoader() {
    const loader = document.getElementById("globalLoader");
    if (loader) loader.style.display = "none";
}

/*------------------------------------------------------------------------------------------*/
// hide when page fully loads
window.addEventListener("load", function () {
    hideLoader();
});

/*------------------------------------------------------------------------------------------*/
// SHOW LOADER ON ALL FORM SUBMITS (Only for unprevented valid forms)
document.addEventListener("submit", function (e) {

    if (e.defaultPrevented) return;

    if (e.target.id === "homeSearchForm") return;

    // skip cancel booking forms
    if (e.target.classList.contains("inline-cancel-form")) {
        return;
    }

    // ❌ SKIP ADMIN LOGIN FORM
    if (e.target.id === "adminLoginForm") {
        return;
    }

    // ✅ ONLY show loader if form is valid
    if (!e.target.checkValidity())
        return;   // ❌ STOP loader

    showLoader();
});

/*------------------------------------------------------------------------------------------*/
// SHOW LOADER ONLY ON REAL PAGE NAVIGATION LINK CLICKS
document.addEventListener("click", function (e) {

    const link = e.target.closest("a");

    if (!link) return;

    // ignore cancel form buttons
    if (link.closest(".inline-cancel-form")) return;

    // ignore download buttons
    if (link.classList.contains("btn-download") || link.hasAttribute("download")) return;

    // ignore links with no-loader class
    if (link.classList.contains("no-loader")) return;

    // ignore links opening in new tab/window
    if (link.getAttribute("target") === "_blank") return;

    // ignore dropdown toggles or elements with data-bs-toggle / data-toggle
    if (link.classList.contains("dropdown-toggle") || link.hasAttribute("data-bs-toggle") || link.hasAttribute("data-toggle")) return;

    // ignore links with inline JS handlers (onclick)
    if (link.hasAttribute("onclick")) return;

    const href = link.getAttribute("href");

    // ignore missing, empty, anchor links (#...), or javascript: calls
    if (!href || href.trim() === "" || href.trim() === "#" || href.startsWith("#") || href.toLowerCase().startsWith("javascript:")) return;

    showLoader();

});

/*------------------------------------------------------------------------------------------*/
// SEARCH BUTTON HANDLER
function handleSearchSubmit(e) {

    const btn = document.getElementById("searchBtn");

    if (btn) {
        btn.disabled = true;
        btn.innerText = "Searching...";
    }

    showLoader();
}

/*------------------------------------------------------------------------------------------*/
// SEAT AVAILABILITY LOADER
function openAvailability(classId, trainId) {

    const isUserClick = true;

    window.trainConnection.invoke("JoinTrain", trainId.toString())
        .catch(err => console.error(err));

    const loader = document.getElementById("loadingModal");
    loader.style.display = "flex";

    const start = Date.now();

    const panel = document.getElementById("panel-" + trainId);
    panel.style.display = "block";

    const boxes = panel.parentElement.querySelectorAll(".class-box");
    boxes.forEach(b => b.classList.remove("active"));

    const selectedBox = panel.parentElement.querySelector(
        `.class-box[data-class="${classId}"]`
    );

    if (selectedBox) selectedBox.classList.add("active");

    const cards = panel.querySelectorAll(".class-card");
    cards.forEach(c => c.classList.remove("active"));

    const target = panel.querySelector(`.class-card[data-class="${classId}"]`);

    if (target) {
        target.style.visibility = "hidden";   // hide entire card
    }

    if (target) {

        target.classList.add("active");

        const status = target.querySelector(".class-status");

        if (status) {
            status.innerText = "Checking...";
            status.style.color = "#555";
        }
    }

    const travelDate = document.getElementById("journeyDatePicker").value;
    const quota = document.getElementById("quota")?.value || "GENERAL";
    //const quota = new URLSearchParams(window.location.search).get("Quota") || "GENERAL";

    fetch(`/Booking/GetSeatAvailability?trainId=${trainId}&classId=${classId}&journeyDate=${travelDate}&quota=${quota}`)
        .then(response => response.json())
        .then(data => {

            // 🚫 BOOKING NOT OPEN YET
            if (data.status === "BOOKING_NOT_ALLOWED") {

                const elapsed = Date.now() - start;
                const delay = Math.max(0, 1000 - elapsed);

                setTimeout(() => {

                    loader.style.display = "none";

                    if (!target) return;

                    const statusEl = target.querySelector(".class-status");
                    const fareEl = target.querySelector(".class-fare");
                    const btnEl = target.querySelector(".book-btn");

                    // ✅ visible
                    target.style.visibility = "visible";

                    if (statusEl) {

                        statusEl.style.visibility = "visible";

                        statusEl.innerText = "BOOKINGS are NOT ALLOWED at this TIME";

                        statusEl.style.color = "red";

                        statusEl.style.fontWeight = "bold";
                    }

                    // ❌ hide fare
                    if (fareEl) {
                        fareEl.style.display = "none";
                    }

                    // ❌ hide button
                    if (btnEl) {
                        btnEl.style.display = "none";
                    }

                }, delay);

                return;
            }

            if (data.status === "ARP_BLOCKED") {

                loader.style.display = "none";

                const statusEl = target.querySelector(".class-status");

                target.style.visibility = "visible";
                statusEl.style.visibility = "visible";

                statusEl.innerText = data.message + " (" + data.code + ")";
                statusEl.style.color = "red";

                const btn = target.querySelector(".book-btn");
                if (btn) {
                    btn.style.display = "none";
                }

                showToast(data.message + " (" + data.code + ")");

                return;
            }

            // 🔴 TRAIN CANCELLED (TOP PRIORITY)
            if (data.status === "TRAIN_CANCELLED") {

                const elapsed = Date.now() - start;
                const delay = Math.max(0, 1000 - elapsed);

                setTimeout(() => {

                    loader.style.display = "none";

                    if (!target) return;

                    const statusEl = target.querySelector(".class-status");
                    const fareEl = target.querySelector(".class-fare");
                    const btnEl = target.querySelector(".book-btn");

                    // ✅ make visible
                    target.style.visibility = "visible";
                    if (statusEl) statusEl.style.visibility = "visible";

                    // 🔥 CHANGE TEXT
                    if (statusEl) {
                        statusEl.innerText = "TRAIN CANCELLED";
                        statusEl.style.color = "red";
                        statusEl.style.fontWeight = "bold";
                    }

                    // ❌ REMOVE AVAILABLE UI
                    if (fareEl) fareEl.style.display = "none";

                    // ❌ BLOCK BOOK BUTTON
                    if (btnEl) {
                        btnEl.style.display = "none";
                    }

                }, delay);

                return; // 🚫 STOP everything else
            }

            // 🚨 TRAIN DEPARTED SIMPLE OVERWRITE
            if (data.status === "TRAIN_DEPARTED") {

                const elapsed = Date.now() - start;
                const delay = Math.max(0, 1000 - elapsed);

                setTimeout(() => {

                    loader.style.display = "none";

                    if (!target) return;

                    const statusEl = target.querySelector(".class-status");
                    const fareEl = target.querySelector(".class-fare");
                    const btnEl = target.querySelector(".book-btn");

                    // ✅ VERY IMPORTANT: MAKE VISIBLE AGAIN
                    target.style.visibility = "visible";
                    if (statusEl) statusEl.style.visibility = "visible";

                    // ✅ overwrite text
                    if (statusEl) {
                        statusEl.innerText = "TRAIN DEPARTED";
                        statusEl.style.color = "red";
                        statusEl.style.fontWeight = "bold";
                    }

                    // ❌ hide others
                    if (fareEl) fareEl.style.display = "none";
                    if (btnEl) btnEl.style.display = "none";

                }, delay);

                return;
            }

            if (data.status === "TATKAL_NOT_ALLOWED") {

                if (!target) return;

                const statusEl = target.querySelector(".class-status");

                // ✅ FIRST: finish loading UI
                loader.style.display = "none";

                target.style.visibility = "visible";
                statusEl.style.visibility = "visible";

                // Show message in UI
                statusEl.innerText = data.message + " (" + data.code + ")";
                statusEl.style.color = "red";

                if (isUserClick) {
                    // 🔥 SHOW TOAST
                    setTimeout(() => {
                        showToast(data.message + " (" + data.code + ")");
                    }, 500);
                }
                    
                // Disable booking button
                const btn = target.querySelector(".book-btn");
                if (btn) {
                    btn.disabled = true;
                    btn.innerText = "NOT AVAILABLE";
                }

                const elapsed = Date.now() - start;
                const minTime = 1000;
                const delay = Math.max(0, minTime - elapsed);

                setTimeout(() => {

                    loader.style.display = "none";

                    target.style.visibility = "visible";
                    statusEl.style.visibility = "visible";

                }, delay);

                return;
            }

            const elapsed = Date.now() - start;
            const minTime = 1000; // keep your delay
            const delay = Math.max(0, minTime - elapsed);

            setTimeout(() => {

                loader.style.display = "none"; // ✅ hide AFTER delay

                if (!target) return;

                let status = "";
                let count = 0;

                if (data.status) {
                    status = data.status;
                    count = data.count || 0;
                }
                else {
                    // fallback if backend not sending status
                    if (data.seatsAvailable > 0) {
                        status = "AVAILABLE";
                        count = data.seatsAvailable;
                    } else if (data.racCount > 0) {
                        status = "RAC";
                        count = data.racCount;
                    } else if (data.wlCount > 0) {
                        status = "WL";
                        count = data.wlCount;
                    } else {
                        status = "NOT_AVAILABLE";
                    }
                }

                const statusEl = target.querySelector(".class-status");

                target.style.visibility = "visible";
                const btnEl = target.querySelector(".book-btn");

                // 🚫 NOT AVAILABLE
                if (status === "NOT_AVAILABLE") {
                    statusEl.innerText = "NOT AVAILABLE";
                    statusEl.style.color = "gray";
                    if (btnEl) btnEl.style.display = "none";
                    return;
                }

                // 🔴 CHART PREPARED
                if (status === "CHART_PREPARED" || status === "CHARTING_DONE") {
                    statusEl.innerText = "CHART PREPARED";
                    statusEl.style.color = "#ea580c";
                    statusEl.style.fontWeight = "bold";
                    if (btnEl) btnEl.style.display = "none";
                    return;
                }

                // 🚫 TRAIN DEPARTED
                if (status === "TRAIN_DEPARTED") {
                    statusEl.innerText = "TRAIN DEPARTED";
                    statusEl.style.color = "#dc2626";
                    statusEl.style.fontWeight = "bold";
                    if (btnEl) btnEl.style.display = "none";
                    return;
                }

                // 🟢 CURR_AVBL
                if (status === "CURR_AVBL") {
                    statusEl.innerText = "CURR_AVBL - " + count;
                    statusEl.style.fontWeight = "bold";
                    statusEl.style.color = "green";
                    if (btnEl) btnEl.style.display = "inline-block";
                    return;
                }

                // 🟢 NORMAL FLOW
                // 🟢 AVAILABLE
                if (status === "AVAILABLE") {

                    const isLocked = (quota === "TATKAL" && !data.isTatkalOpen);

                    let text = "AVAILABLE - " + count;

                    if (isLocked) {
                        text += "#";
                    }

                    statusEl.innerText = text;
                    statusEl.style.color = "green";

                    if (btnEl) {

                        if (isLocked) {
                            btnEl.style.display = "none";
                        } else {
                            btnEl.style.display = "inline-block";
                            btnEl.display = false;
                        }
                    }
                }

                // 🟠 RAC  ✅ CORRECT PLACE
                else if (status === "RAC") {

                    statusEl.innerText = "RAC - " + count;
                    statusEl.style.color = "orange";

                    if (btnEl) {
                        btnEl.style.display = "inline-block";
                        btnEl.disabled = false;
                    }
                }

                // 🔴 WL
                else if (status === "WL") {

                    const WL_LIMIT = 10;

                    if (count > WL_LIMIT) {
                        statusEl.innerText = "NOT AVAILABLE";
                        statusEl.style.color = "gray";

                        if (btnEl) btnEl.style.display = "none";
                        return;
                    }

                    if (status === "WL" && count == 0) {
                        statusEl.innerText = "AVAILABLE";
                        statusEl.style.color = "green";
                    }
                    else {
                        statusEl.innerText = "WL - " + count;
                        statusEl.style.color = "red";
                    }
               

                    if (btnEl) {
                        btnEl.style.display = "inline-block";
                        btnEl.disabled = false;
                    }
                }

                // 🔴 TATKAL → NOT AVAILABLE
                else if (quota === "TATKAL") {

                    statusEl.innerText = "NOT AVAILABLE";
                    statusEl.style.color = "gray";

                    if (btnEl) btnEl.style.display = "none";
                    else {
                        statusEl.innerText = "NOT AVAILABLE";
                        statusEl.style.color = "gray";

                        if (btnEl) btnEl.style.display = "none";
                    }
                }

                // 🟠 GENERAL → WL WITH LIMIT
                const timeEl = target.querySelector(".last-updated");
                if (timeEl) {
                    timeEl.innerText = "Updated just now";
                }

            }, delay);
        })

        .catch(err => {
            loader.style.display = "none";
            console.error("Availability fetch error:", err);
        });
}

/*------------------------------------------------------------------------------------------*/
/* SHOW ON CLICK THE CLASSES AS PER THE TME THAT CHART PREPARED / NOT AVAILABLE */
$(document).on("click", ".book-btn", function (e) {

    const btn = this;

    const card = btn.closest(".class-card");
    if (!card) return;

    const statusEl = card.querySelector(".class-status");
    if (!statusEl) return;

    const text = statusEl.innerText;

    // 🚫 BLOCK booking if chart prepared
    if (text.includes("CHART PREPARED") || text.includes("NOT AVAILABLE") || text.includes("TRAIN CANCELLED")) {

        e.preventDefault();

        showToast("Booking is not allowed for selected class");

        return false;
    }

});

/*------------------------------------------------------------------------------------------*/
// CLASS TAB SWITCH
function showClass(event, code, trainId) {

    //refreshAvailability(trainId, code);

    const container = event.target.closest(".train-card-classes");

    const panel = container.querySelector(".class-panels");
    panel.style.display = "block";

    const cards = container.querySelectorAll(".class-card");
    cards.forEach(c => c.classList.remove("active"));

    const target = container.querySelector(
        `.class-card[data-class="${code}"][data-train="${trainId}"]`
    );

    if (target) {
        target.classList.add("active");
    }

    const tabs = container.querySelectorAll(".class-tab");
    tabs.forEach(t => t.classList.remove("active"));

    event.target.classList.add("active");
}

/*--------------------------------------------------------------------------------------------*/
/* ON CLICK THE CLASSES THE SEATS AVAILABILITY WILL REFRESH DYNAMICALLY FROM THE BACKENED */
function refreshAvailability(trainId, classCode) {

    const travelDate = document.getElementById("travelDate").value;

    $.get("/Train/GetAvailability", {
        trainId: trainId,
        classCode: classCode,
        travelDate: travelDate
    }, function (data) {

        const seatElement = document.getElementById(
            "availability-" + trainId + "-" + classCode
        );

        if (seatElement) {

            if (data.availableSeats > 0) {
                seatElement.innerText = "AVAILABLE - " + data.availableSeats;
                seatElement.style.color = "green";
            } else {
                seatElement.innerText = "NOT AVAILABLE";
                seatElement.style.color = "red";
            }
        }

    });
}

/*-------------------------------------------------------------------------------------------------*/
/* WARNING / INFO THAT YOU ARE CHANGING THE DETAILS / EDITING THE DETAILS OF THE PASSENEGERS */
$(document).on("click", ".editPassengerBtn", function () {

    if (this.disabled) return;

    let btn = $(this);

    Swal.fire({
        title: "Are you sure?",
        text: "You are about to edit passenger details. This cannot be reverted.",
        icon: "warning",
        showCancelButton: true,
        confirmButtonText: "Yes, continue"
    }).then((result) => {

        if (!result.isConfirmed) return;

        Swal.fire({
            title: "Proceed",
            text: "You can review changes before saving.",
            icon: "info",
            showCancelButton: true,
            confirmButtonText: "OK",
            cancelButtonText: "Cancel"
        }).then((result) => {

            // ❌ If cancel clicked -> STOP here
            if (!result.isConfirmed) return;

            // ✅ ONLY runs when OK clicked
            let row = btn.closest("tr");

            let passengerId = btn.data("id");

            let name = row.find("td").eq(1).text().trim();
            let age = row.find("td").eq(2).text().trim();
            let gender = row.find("td").eq(3).text().trim();

            $("#editPassengerId").val(passengerId);
            $("#editName").val(name);
            $("#editAge").val(age);
            $("#editGender").val(gender);

            $("#editPassengerPopup").css("display", "flex");
        });
    });
});
function closeEditPopup() {
    $("#editPassengerPopup").hide();
}

/*------------------------------------------------------------------------------------------*/
/* IN MY BOOKING PAGE EDIT OPTION FOR THE PASSENGERS */
function confirmPassengerEdit() {

    let id = $("#editPassengerId").val();
    let name = $("#editName").val();
    let age = $("#editAge").val();
    let gender = $("#editGender").val();

    if (!name || !age) {
        if (typeof showToast === 'function') showToast("Please fill in valid name and age");
        else alert("Please fill in valid name and age");
        return;
    }

    fetch('/Booking/EditPassenger', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            Id: id,
            Name: name,
            Age: age,
            Gender: gender
        })
    })
        .then(res => res.json())
        .then(data => {

            if (data.success) {
                if (typeof Swal !== 'undefined') {
                    Swal.fire({
                        icon: 'success',
                        title: 'Updated!',
                        text: data.message || 'Passenger details updated successfully.',
                        timer: 2000,
                        showConfirmButton: false
                    });
                } else {
                    alert(data.message);
                }

                // update table values immediately
                let row = $("#passenger-row-" + id);

                row.find("td").eq(1).text(name);
                row.find("td").eq(2).text(age);
                row.find("td").eq(3).text(gender);

                // disable edit button
                let btn = row.find(".editPassengerBtn");

                btn.prop("disabled", true);
                btn.text("Edited");
                btn.attr("title", "Editing not allowed / already edited");

                closeEditPopup();
                window.bookingHasBeenUpdated = true;
            } else {
                if (typeof Swal !== 'undefined') {
                    Swal.fire('Error', data.message || 'Update failed', 'error');
                } else {
                    alert(data.message);
                }
            }

        })
        .catch(err => {
            console.error(err);
            if (typeof Swal !== 'undefined') {
                Swal.fire('Error', 'An error occurred while saving', 'error');
            } else {
                alert('An error occurred while saving');
            }
        });
}


/* SWAL FIRE POP UPS */
// 🔴 WARNING CONFIRM (for dangerous actions)
function showWarningConfirm(message) {
    return Swal.fire({
        title: "Are you sure?",
        text: message,
        icon: "warning",
        showCancelButton: true,
        confirmButtonText: "Yes",
        cancelButtonText: "Cancel",
        allowOutsideClick: false
    });
}

// 🟡 INFO CONFIRM (for proceed step)
function showInfoConfirm(message) {
    return Swal.fire({
        title: "Proceed",
        text: message,
        icon: "info",
        showCancelButton: true,
        confirmButtonText: "OK",
        cancelButtonText: "Cancel",
        allowOutsideClick: false
    });
}

// ✅ SUCCESS ALERT
function showSuccess(message) {
    return Swal.fire({
        icon: "success",
        title: "Success",
        text: message
    });
}

// ❌ ERROR ALERT
function showError(message) {
    return Swal.fire({
        icon: "error",
        title: "Error",
        text: message
    });
}

/*------------------------------------------------------------------------------------------*/
/* ALL THE ERRORS ARE SHOWN BECAUSE OF THIS TO THE TOP RIGHT OF THE PAGE */
/*function showToast(message, type = "error") {
    const container = document.getElementById("toastContainer");

    const toast = document.createElement("div");
    toast.className = "toast";

    toast.innerHTML = `
        <span class="close-btn">&times;</span>
        <strong>Error!</strong>
        <div>${message}</div>
    `;

    container.appendChild(toast);

    toast.querySelector(".close-btn").onclick = () => {
        toast.remove();
    };

    setTimeout(() => {
        toast.remove();
    }, 10000);
}*/

function showToast(message, type = "error") {

    const container = document.getElementById("toastContainer");

    const toast = document.createElement("div");

    // ✅ Add type class
    toast.className = "toast " + type;

    // ✅ Dynamic title
    let title = type === "success" ? "Success!" : "Error!";

    toast.innerHTML = `
        <span class="close-btn">&times;</span>
        <strong>${title}</strong>
        <div>${message}</div>
    `;

    container.appendChild(toast);

    toast.querySelector(".close-btn").onclick = () => {
        toast.remove();
    };

    setTimeout(() => {
        toast.remove();
    }, 10000);
}

$(document).on("input change", "input, select", function () {
    $(this).removeClass("input-error");
});

$(document).on("input change", "#modifySearchForm input", function () {
    $(this).removeClass("input-error");
});

$(document).on("input change", "#homeSearchForm input", function () {
    $(this).removeClass("input-error");
});

/*------------------------------------------------------------------------------------------*/
/* IN CHECKOUT PAGE VALIDATIONS ERRORS SHOW */
function showValidationErrors(form) {

    const firstInvalid = form.querySelector(":invalid");

    if (firstInvalid) {

        let message = "";  

        if (firstInvalid.name.includes("passengerNames")) {
            message = "Please enter passenger name";
        }
        else if (firstInvalid.name.includes("passengerAges")) {
            message = "Please enter valid age";
        }
        else {
            message = "Please enter the required inputs";
        }

        showToast(message);

        firstInvalid.focus();
        firstInvalid.classList.add("input-error");
    }
}

/*-----------------------------------------------------------------------------------------------*/
/* IN TRAIN RESULTS PAGE MODIFY BUTTON ERROR SHOW IF STATIONS ARE EMPTY */
$(document).off("submit", "#modifySearchForm").on("submit", "#modifySearchForm", function (e) {

    // ✅ PREVENT DOUBLE EXECUTION
    if ($(this).data("submitted")) {
        e.preventDefault();
        return false;
    }

    const form = this;
    const btn = $(this).find("button[type='submit']");

    // ❌ INVALID FORM
    if (!form.checkValidity()) {
        e.preventDefault();
        e.stopImmediatePropagation();

        const firstInvalid = form.querySelector(":invalid");

        if (firstInvalid) {
            showToast("Please enter the required inputs");
            firstInvalid.focus();
            firstInvalid.classList.add("input-error");
        }

        return false;
    }

    // ❌ STATION NOT SELECTED
    if (!$('#fromStationId').val() || !$('#toStationId').val()) {
        e.preventDefault();
        showToast("Please select stations from the list");
        return false;
    }

    // ✅ MARK AS SUBMITTED (only once)
    $(this).data("submitted", true);

    // ❌ SAME STATION
    if ($('#fromStationId').val() === $('#toStationId').val()) {
        e.preventDefault();
        hideLoader();
        //showToast("From and To stations cannot be same");
        return false;
    }

    // ✅ ONLY HERE → disable button + loader
    btn.prop("disabled", true).text("Processing...");
    showLoader();
});

/*------------------------------------------------------------------------------------------*/
/* ERRORS TO SHOWN IN THE HOME PAGE I.E., INDEX PAGE */
$(document).off("submit", "#homeSearchForm").on("submit", "#homeSearchForm", function (e) {

    const form = this;
    const btn = $(this).find("button[type='submit']");

    // 🔴 Tatkal rule (MOVED HERE)
    if (window.isTatkal && !window.isLoggedIn) {
        e.preventDefault();
        window.location.replace("/Account/Login");
        return false;
    }

    // ❌ HTML validation
    if (!form.checkValidity()) {
        e.preventDefault();
        e.stopImmediatePropagation();

        const firstInvalid = form.querySelector(":invalid");

        if (firstInvalid) {
            showToast("Please enter the required inputs");
            firstInvalid.focus();
            firstInvalid.classList.add("input-error");
        }

        return false;
    }

    // ❌ Station validation
    if (!$('#fromStationId').val() || !$('#toStationId').val()) {
        e.preventDefault();
        e.stopImmediatePropagation();
        e.stopPropagation();
        showToast("Please select stations from the list");
        return false;
    }

    if ($('#fromStationId').val() === $('#toStationId').val()) {
        e.preventDefault();
        e.stopImmediatePropagation();
        e.stopPropagation();
        showToast("From and To stations cannot be same");
        return false;
    }

    // ✅ VALID
    btn.prop("disabled", true).text("Searching...");
    showLoader();
});

/*------------------------------------------------------------------------------------------*/
/* ADMIN LOGIN FORM ON & OFF VALIDATION */
$(document).off("submit", "#adminLoginForm").on("submit", "#adminLoginForm", function (e) {

    const form = this;
    const btn = $(this).find("button[type='submit']");

    // ❌ INVALID
    if (!form.checkValidity()) {
        e.preventDefault();
        e.stopImmediatePropagation();   // 🔥 IMPORTANT

        showValidationErrors(form);     // ✅ reuse your function

        return false;
    }

    // ✅ VALID
    btn.prop("disabled", true).text("Logging in...");
    showLoader();
});

/*------------------------------------------------------------------------------------------*/
/* ADMIN PAGE VALIDATION */
$(document).off("submit", "#adminLoginForm")
    .on("submit", "#adminLoginForm", function (e) {

        const form = this;
        const btn = $(this).find("button[type='submit']");

        if (!form.checkValidity()) {
            e.preventDefault();
            e.stopImmediatePropagation();

            // 🔥 DEBUG (add this temporarily)
            console.log("Admin validation triggered");

            showValidationErrors(form);   // ✅ THIS SHOULD RUN

            return false;
        }

        btn.prop("disabled", true).text("Logging in...");
        showLoader();
    });

/*------------------------------------------------------------------------------------------*/
function startButtonLoader() {
    const btn = document.getElementById("loginBtn");
    const text = document.getElementById("btnText");
    const loader = document.getElementById("btnLoader");

    text.style.visibility = "hidden";   // hide text
    loader.style.display = "inline-block";
    btn.disabled = true;
}

/* ON CLICK LOGIN BUTTON SHOW lOGGING IN........ */
$(document).off("submit", "#loginForm").on("submit", "#loginForm", function (e) {

    const form = this;
    const btn = $(this).find("button[type='submit']");

    // ❌ INVALID FORM
    if (!form.checkValidity()) {
        e.preventDefault();
        e.stopImmediatePropagation();

        if (!showLoginValidation(form)) {
            e.preventDefault();
            e.stopImmediatePropagation();
            return false;
        }
        return false;
    }

    // ✅ VALID
    btn.prop("disabled", true).text("Logging in...");
    showLoader();
});

function showLoginValidation(form) {

    const username = form.querySelector("input[name='Username']");
    const password = form.querySelector("input[name='Password']");
    const captcha = form.querySelector("input[name='CaptchaInput']");

    if (!username.value.trim()) {
        showToast("Please enter username");
        username.focus();
        username.classList.add("input-error");
        return false;
    }

    if (!password.value.trim()) {
        showToast("Please enter password");
        password.focus();
        password.classList.add("input-error");
        return false;
    }

    if (!captcha.value.trim()) {
        showToast("Please enter captcha");
        captcha.focus();
        captcha.classList.add("input-error");
        return false;
    }

    return true;
}

/* ADMIN LOGIN PAGE TOAST FOR WRONG CREDENTIALS */
/*document.addEventListener("DOMContentLoaded", function () {

    const loginForm = document.querySelector("form[action='/Account/Login']");

    if (loginForm) {
        loginForm.addEventListener("submit", function (e) {

            const email = loginForm.querySelector("input[name='Email']").value.trim();
            const password = loginForm.querySelector("input[name='Password']").value.trim();
            const captcha = loginForm.querySelector("input[name='CaptchaInput']").value.trim();

            if (!email || !password || !captcha) {
                e.preventDefault(); // 🔥 stop form

                showToast("Please enter the required inputs");

                return false;
            }
        });
    }

});*/

/* LOGIN PAGE PASSWORD TOGGLE */
function togglePasswordVisibility() {
    const passwordField = document.getElementById('passwordInput');
    const icon = document.querySelector('#togglePassword i');

    if (!passwordField || !icon) return;

    if (passwordField.type === 'password') {
        passwordField.type = 'text';
        icon.classList.remove('fa-eye');
        icon.classList.add('fa-eye-slash');
    } else {
        passwordField.type = 'password';
        icon.classList.remove('fa-eye-slash');
        icon.classList.add('fa-eye');
    }
}

/* OPEN & CLOSE FARE POP UP ( TRAIN RESUTLS PAGE - CLASS CARD (i) ) */
// 🔥 OPEN / CLOSE POPUP
$(document).on("click", ".fare-toggle", function (e) {

    e.stopPropagation();

    const parent = this.closest(".fare-info");
    const modal = parent.querySelector(".fare-modal");

    // 🔥 close all others first
    document.querySelectorAll(".fare-modal").forEach(m => {
        if (m !== modal) m.classList.remove("show");
    });

    // 🔥 toggle current
    modal.classList.toggle("show");
});

$(document).on("click", function () {
    document.querySelectorAll(".fare-modal").forEach(m => {
        m.classList.remove("show");
    });
});

$(document).on("click", ".fare-modal", function (e) {
    e.stopPropagation();
});

/**/
